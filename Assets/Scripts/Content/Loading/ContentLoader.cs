using System;
using System.Collections.Generic;
using System.Linq;
using TCGinFU.Content.Json;
using TCGinFU.Core;

namespace TCGinFU.Content.Loading
{
    /// <summary>
    /// 把内容包 JSON 解析成 Core 的 POCO。
    ///
    /// **本类不碰文件系统**：只接受已经读好的字符串，由调用方决定内容是
    /// <c>File.ReadAllText</c> 来的还是 <c>TextAsset.text</c> 来的。
    /// 这样 <c>TCGinFU.Content</c> 的 <c>noEngineReferences</c> 保得住，
    /// 测试也不需要 mock 文件系统（见 data_architecture.md §3）。
    /// </summary>
    public static class ContentLoader
    {
        /// <summary>按 <c>pack.json</c> 的清单顺序加载一个内容包。</summary>
        /// <param name="readFile">相对内容包根目录的路径 → 文件文本。读不到应返回 null。</param>
        public static void LoadPack(ContentDatabase db, Func<string, string> readFile, LoadReport report)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (readFile == null) throw new ArgumentNullException(nameof(readFile));
            report ??= new LoadReport();

            JsonValue manifest = ReadJson(readFile, "pack.json", report);
            if (!manifest.Exists) return;

            int schema = manifest["schemaVersion"].AsInt(1);
            if (schema != SupportedSchemaVersion)
            {
                report.Errors.Add(
                    $"pack.json：schemaVersion {schema} 与当前支持的 {SupportedSchemaVersion} 不符");
                return;
            }

            db.DefaultCardBackArtId = Blank(manifest["defaultCardBack"].AsString());

            foreach (KeyValuePair<string, JsonValue> entry in manifest["artFallback"].Fields)
            {
                string artId = Blank(entry.Value.AsString());
                if (artId != null) db.AddArtFallback(entry.Key, artId);
            }

            // 脚本不是 JSON，本类只记下清单；真正执行它们的是 TCGinFU.Scripting，
            // 那是个带 MoonSharp 依赖的程序集，不该被纯数据加载器拖进来
            foreach (JsonValue entry in manifest["scripts"].Items)
            {
                string rel = entry.AsString();
                if (rel == null) { report.Errors.Add("pack.json 的 scripts 里有非字符串项"); continue; }
                db.AddScriptPath(rel);
            }

            // 顺序有意义：卡牌必须先于卡组加载，卡组展开时要按 Id 找卡
            LoadList(db, readFile, report, manifest["statuses"], "statuses", LoadStatuses);
            LoadList(db, readFile, report, manifest["cards"], "cards", LoadCards);
            LoadList(db, readFile, report, manifest["characters"], "characters", LoadCharacters);
            LoadList(db, readFile, report, manifest["enemies"], "enemies", LoadEnemies);
            LoadList(db, readFile, report, manifest["decks"], "decks", LoadDecks);
        }

        public const int SupportedSchemaVersion = 1;

        private static void LoadList(
            ContentDatabase db, Func<string, string> readFile, LoadReport report,
            JsonValue paths, string label,
            Action<ContentDatabase, JsonValue, string, LoadReport> load)
        {
            foreach (JsonValue entry in paths.Items)
            {
                string rel = entry.AsString();
                if (rel == null) { report.Errors.Add($"pack.json 的 {label} 里有非字符串项"); continue; }

                JsonValue root = ReadJson(readFile, rel, report);
                if (root.Exists) load(db, root, rel, report);
            }
        }

        private static JsonValue ReadJson(Func<string, string> readFile, string rel, LoadReport report)
        {
            string text;
            try
            {
                text = readFile(rel);
            }
            catch (Exception e)
            {
                report.Errors.Add($"{rel}：读取失败 — {e.Message}");
                return JsonValue.Missing;
            }

            if (text == null) { report.Errors.Add($"{rel}：文件不存在"); return JsonValue.Missing; }

            try
            {
                return Json.Json.Parse(text);
            }
            catch (JsonException e)
            {
                report.Errors.Add($"{rel}：{e.Message}");
                return JsonValue.Missing;
            }
        }

        // ───────────────────────────── 卡牌 ─────────────────────────────

        private static void LoadCards(ContentDatabase db, JsonValue root, string file, LoadReport report)
        {
            foreach (JsonValue node in root["cards"].Items)
            {
                CardDefinition card = ReadCard(node, file, report);
                if (card != null) db.Put(card);
            }
        }

        private static CardDefinition ReadCard(JsonValue node, string file, LoadReport report)
        {
            string id = node["id"].AsString();
            if (string.IsNullOrWhiteSpace(id)) { report.Errors.Add($"{file}：有卡牌缺少 id"); return null; }

            string where = $"{file} / {id}";
            var errors = report.Errors;

            var colors = new List<AttributeColor>();
            foreach (JsonValue c in node["colors"].Items)
            {
                colors.Add(c.AsEnum(AttributeColor.Might, $"{where} 的 colors", errors));
            }

            if (colors.Count > 2) errors.Add($"{where}：颜色最多 2 项，当前 {colors.Count} 项");

            // 「可选消耗几个充能」。声明在卡上，界面据此自动生成提问（见 CardDefinition.ChargeOptions）
            var chargeOptions = new List<int>();
            foreach (JsonValue option in node["chargeOptions"].Items)
            {
                int count = option.AsInt(-1);
                if (count < 0) { errors.Add($"{where} 的 chargeOptions：{option.AsString("(非数字)")} 不是非负整数"); continue; }
                if (!chargeOptions.Contains(count)) chargeOptions.Add(count);
            }

            chargeOptions.Sort();

            CardRole role = ReadRole(node, where, errors);

            return new CardDefinition(
                id,
                node["name"].AsString(id),
                node["category"].AsEnum(CardCategory.Class, $"{where} 的 category", errors),
                ReadPlay(node, role, where, errors))
            {
                Role = role,
                FormId = ReadFormId(node, role, where, errors),
                IsHeroSkill = node["hero"].AsBool(),
                Colors = colors,
                ApCost = node["ap"].AsInt(),
                MpCost = node["mp"].AsInt(),
                ChargeCost = node["charge"].AsInt(),
                ChargeOptions = chargeOptions,
                HasCrit = node["hasCrit"].AsBool(),
                // 迁移时 ranged 落在 unmapped 里，现在它是正式字段了；两处都认，老包不用改
                IsRanged = node["ranged"].Exists ? node["ranged"].AsBool() : node["unmapped"]["ranged"].AsBool(),
                RequiresBackRow = node["requiresBackRow"].Exists
                    ? node["requiresBackRow"].AsBool()
                    : node["unmapped"]["requires_back_row"].AsBool(),
                Targeting = node["targeting"].AsEnum(CardTargeting.Auto, $"{where} 的 targeting", errors),
                ClassId = Blank(node["classId"].AsString()),
                RulesText = node["rulesText"].AsString(),
                ArtId = Blank(node["art"].AsString()),
                BackArtId = Blank(node["backArt"].AsString()),
                BehaviourId = Blank(node["behaviourId"].AsString()),
                WeaponBaseDamage = node["weaponBaseDamage"].AsInt(),
                ArmorBaseShield = node["armorBaseShield"].AsInt(),
                TwoHanded = node["twoHanded"].AsBool(),
                Element = node["element"].AsEnum(DamageElement.Physical, $"{where} 的 element", errors),
                ShieldKind = node["shieldKind"].AsEnum(ShieldKind.Physical, $"{where} 的 shieldKind", errors),
                TransformTrigger = node["transformTrigger"]
                    .AsEnum(TransformTrigger.Conditional, $"{where} 的 transformTrigger", errors),
                TransformConditionText = node["transformConditionText"].AsString(),
                Effects = node["effects"].Items
                    .Select((e, i) => ReadEffect(e, $"{where} 的 effects[{i}]", errors))
                    .ToArray(),
            };
        }

        /// <summary>
        /// 读 <c>role</c>：打法之外的标记（§7.1）。缺省是普通牌。
        /// <para><c>EnemyIntent</c> 不接受手写——敌人意图只能来自 <c>enemies/*.json</c>，
        /// 让卡牌文件里出现一张「敌人牌」只会让它既进不了卡组也没人宣告。</para>
        /// </summary>
        private static CardRole ReadRole(JsonValue node, string where, List<string> errors)
        {
            if (!node["role"].Exists) return CardRole.Normal;

            CardRole role = node["role"].AsEnum(CardRole.Normal, $"{where} 的 role", errors);
            if (role == CardRole.EnemyIntent)
            {
                errors.Add($"{where}：role 不能手写成 EnemyIntent，敌人意图只能定义在 enemies/*.json 里");
                return CardRole.Normal;
            }

            return role;
        }

        /// <summary>
        /// 读 <c>play</c>：打法（§7.1）。升变牌不按打法结算，所以它既不该有也不需要；
        /// 其余的牌**必须**写明——缺省成「技能」会让一张写着「受击时打出」的牌在响应窗口被拒收。
        /// </summary>
        private static CardPlay? ReadPlay(JsonValue node, CardRole role, string where, List<string> errors)
        {
            if (role == CardRole.Transform)
            {
                if (node["play"].Exists) errors.Add($"{where}：升变牌不按打法结算，不要写 play");
                return null;
            }

            if (!node["play"].Exists)
            {
                // 2026-11-23 之前这里写的是 kind，一个字段同时管打法和标记。
                // 老包落到这条错误上，所以顺带把改法说清楚
                errors.Add(
                    $"{where}：缺少 play（Skill / Timing / Continuous）"
                    + "。老包的 kind 已拆成 play 与 role（Transform / Form）");
                return CardPlay.Skill;
            }

            return node["play"].AsEnum(CardPlay.Skill, $"{where} 的 play", errors);
        }

        /// <summary>读 <c>formId</c>：形态牌所属的形态卡组。两个方向都要对得上。</summary>
        private static string ReadFormId(JsonValue node, CardRole role, string where, List<string> errors)
        {
            string formId = Blank(node["formId"].AsString());

            if (role == CardRole.Form && formId == null)
            {
                errors.Add($"{where}：形态牌缺少 formId，无法确认它属于哪一组形态");
            }
            else if (role != CardRole.Form && formId != null)
            {
                errors.Add($"{where}：只有 role 为 Form 的牌才能写 formId");
            }

            return formId;
        }

        /// <summary>
        /// 把一条意图读成一张**敌人的牌**。
        ///
        /// <para>敌人的意图不是另一套机制，所以它的效果也该是 <see cref="EffectData"/>、
        /// 行为也该是同一套 <see cref="ICardBehaviour"/>。这里顺手把老数据里
        /// <c>unmapped.mp</c> 的费用提成正式字段——「以太虹吸消耗10MP」写在卡面上却不生效，
        /// 是老迁移留下的坑。</para>
        /// </summary>
        private static CardDefinition ReadEnemyCard(
            JsonValue node, string intentId, string where, List<string> errors)
        {
            return new CardDefinition(
                intentId,
                node["name"].AsString(intentId),
                CardCategory.Class,
                // 意图由敌人按阶段宣告，不走玩家的打法体系
                play: null)
            {
                Role = CardRole.EnemyIntent,
                MpCost = node["mp"].Exists ? node["mp"].AsInt() : node["unmapped"]["mp"].AsInt(),
                RulesText = node["text"].AsString(),
                BehaviourId = Blank(node["behaviourId"].AsString()),
                Element = node["element"].AsEnum(
                    DamageElement.Physical, $"{where} 的意图 {intentId} 的 element", errors),
                Effects = node["effects"].Items
                    .Select((e, i) => ReadEffect(e, $"{where} 的意图 {intentId} 的 effects[{i}]", errors))
                    .ToArray(),
            };
        }

        // ───────────────────────────── 状态 ─────────────────────────────

        /// <summary>
        /// 状态的数值属于状态本身，不属于挂它的那张卡——
        /// 否则每张挂流血的卡都要把「每层每轮 2 点」重抄一遍，改平衡时漏一张就不一致了。
        /// </summary>
        private static void LoadStatuses(ContentDatabase db, JsonValue root, string file, LoadReport report)
        {
            foreach (JsonValue node in root["statuses"].Items)
            {
                string id = node["id"].AsString();
                if (string.IsNullOrWhiteSpace(id)) { report.Errors.Add($"{file}：有状态缺少 id"); continue; }

                string where = $"{file} 的状态 {id}";

                db.AddStatus(new StatusDefinition(id)
                {
                    DamagePerRound = node["damagePerRound"].AsInt(),
                    HealPerRound = node["healPerRound"].AsInt(),
                    DotElement = node["dotElement"].AsEnum(DamageElement.Poison, $"{where} 的 dotElement", report.Errors),
                    OutgoingDamageBonus = node["outgoingDamageBonus"].AsInt(),
                    IncomingDamageReduction = node["incomingDamageReduction"].AsInt(),
                    IncomingDamageBonus = node["incomingDamageBonus"].AsInt(),
                    ConsumedOnTrigger = node["consumedOnTrigger"].AsBool(),
                    BehaviourId = Blank(node["behaviourId"].AsString()),
                    CritResistDelta = node["critResistDelta"].AsInt(),
                    IncomingMultiplierNumerator = node["incomingMultiplier"][0].AsInt(),
                    IncomingMultiplierDenominator = node["incomingMultiplier"][1].AsInt(),

                    // 不写就是「不改克制」。不能给个默认属性顶上——
                    // 那会让每个状态都顺手把宿主变成物理易损
                    VulnerableElement = Blank(node["vulnerableElement"].AsString()) == null
                        ? null
                        : node["vulnerableElement"].AsEnum(
                            DamageElement.Physical, $"{where} 的 vulnerableElement", report.Errors),
                }, report, file);
            }
        }

        private static EffectData ReadEffect(JsonValue node, string where, List<string> errors)
        {
            JsonValue amount = node["amount"];
            int times = amount["times"].AsInt(1);
            int dividedBy = amount["dividedBy"].AsInt(1);
            if (times == 0) { errors.Add($"{where}：amount.times 不能为 0"); times = 1; }
            if (dividedBy <= 0) { errors.Add($"{where}：amount.dividedBy 必须为正"); dividedBy = 1; }

            EffectKind kind = node["kind"].AsEnum(EffectKind.Damage, $"{where} 的 kind", errors);
            TargetScope scope = node["target"].AsEnum(TargetScope.SingleEnemy, $"{where} 的 target", errors);

            return new EffectData
            {
                Kind = kind,
                Amount = new ValueExpr(
                    amount["source"].AsEnum(ValueSource.Flat, $"{where} 的 amount.source", errors),
                    amount["add"].AsInt(), times, dividedBy),
                // 援护改写的是「本次结算的既定目标」，所以作用域强制成 CurrentTarget，忽略 JSON 里写的 target
                Target = new TargetSelector(kind == EffectKind.Cover ? TargetScope.CurrentTarget : scope),
                Element = node["element"].AsEnum(DamageElement.Physical, $"{where} 的 element", errors),
                ShieldKind = node["shieldKind"].AsEnum(ShieldKind.Physical, $"{where} 的 shieldKind", errors),
                StatusId = Blank(node["statusId"].AsString()),
                Duration = node["duration"].AsInt(1),
                Condition = node["condition"].AsEnum(EffectCondition.Always, $"{where} 的 condition", errors),
                RollsOwnCrit = node["rollsOwnCrit"].AsBool(),
                // 不写 rowTarget 就是「在前后排之间切换」；写了就是定向移动
                RowTarget = node["rowTarget"].Exists
                    ? node["rowTarget"].AsEnum(Row.Back, $"{where} 的 rowTarget", errors)
                    : (Row?)null,
                // 去哪一排由卡面定死，排内站哪让玩家挑（裁定 R-17c）
                PlayerChoosesSlot = node["playerChoosesSlot"].AsBool(),
            };
        }

        // ───────────────────────────── 角色 ─────────────────────────────

        private static void LoadCharacters(ContentDatabase db, JsonValue root, string file, LoadReport report)
        {
            foreach (JsonValue node in root["characters"].Items)
            {
                CharacterContent c = ReadCharacter(db, node, file, report);
                if (c != null) db.Put(c);
            }
        }

        private static CharacterContent ReadCharacter(
            ContentDatabase db, JsonValue node, string file, LoadReport report)
        {
            string id = node["id"].AsString();
            if (string.IsNullOrWhiteSpace(id)) { report.Errors.Add($"{file}：有角色缺少 id"); return null; }

            string where = $"{file} / {id}";
            var errors = report.Errors;

            var classes = new List<ClassLevel>();
            foreach (JsonValue c in node["classes"].Items)
            {
                string classId = c["classId"].AsString();
                if (string.IsNullOrWhiteSpace(classId)) { errors.Add($"{where}：职业项缺少 classId"); continue; }
                classes.Add(new ClassLevel(classId, c["level"].AsInt(1)));
            }

            if (classes.Count == 0) { errors.Add($"{where}：角色至少要有 1 个职业"); return null; }

            JsonValue dice = node["dice"];
            var attributeDice = new AttributeDice(
                dice["Might"].AsEnum(DieRank.D6, $"{where} 的 dice.Might", errors),
                dice["Insight"].AsEnum(DieRank.D6, $"{where} 的 dice.Insight", errors),
                dice["Dexterity"].AsEnum(DieRank.D6, $"{where} 的 dice.Dexterity", errors),
                dice["Willpower"].AsEnum(DieRank.D6, $"{where} 的 dice.Willpower", errors));

            CharacterSheet sheet;
            try
            {
                sheet = new CharacterSheet(
                    node["name"].AsString(id), classes, attributeDice,
                    node["hpBonus"].AsInt(), node["mpBonus"].AsInt());
            }
            catch (ArgumentException e)
            {
                errors.Add($"{where}：{e.Message}");
                return null;
            }

            return new CharacterContent
            {
                Id = id,
                DisplayName = node["name"].AsString(id),
                Subtitle = node["subtitle"].AsString(""),
                Sheet = sheet,
                // R-19：装备档案直接从卡牌定义派生，数值不另记一份
                Weapon = EquipFrom(db, node["weaponCardId"].AsString(), where, errors),
                Armor = ArmorFrom(db, node["armorCardId"].AsString(), where, errors),
                StartingRow = node["startingRow"].AsEnum(Row.Front, $"{where} 的 startingRow", errors),
                ChargeCapacity = node["chargeCapacity"].AsInt(2),
                DeckId = node["deckId"].AsString(),
                PortraitId = Blank(node["portrait"].AsString()),
                LegacyHp = node["legacy"]["hp"].AsInt(),
                LegacyMp = node["legacy"]["mp"].AsInt(),
            };
        }

        private static WeaponProfile EquipFrom(
            ContentDatabase db, string cardId, string where, List<string> errors)
        {
            if (string.IsNullOrEmpty(cardId)) return WeaponProfile.Unarmed;

            CardDefinition card = db.Card(cardId);
            if (card == null) { errors.Add($"{where}：找不到武器牌 {cardId}"); return WeaponProfile.Unarmed; }
            if (card.Category != CardCategory.Weapon)
            {
                errors.Add($"{where}：{cardId} 不是武器牌（category = {card.Category}）");
                return WeaponProfile.Unarmed;
            }

            return WeaponProfile.FromCard(card);
        }

        private static ArmorProfile ArmorFrom(
            ContentDatabase db, string cardId, string where, List<string> errors)
        {
            if (string.IsNullOrEmpty(cardId)) return ArmorProfile.Unarmored;

            CardDefinition card = db.Card(cardId);
            if (card == null) { errors.Add($"{where}：找不到防具牌 {cardId}"); return ArmorProfile.Unarmored; }
            if (card.Category != CardCategory.Armor)
            {
                errors.Add($"{where}：{cardId} 不是防具牌（category = {card.Category}）");
                return ArmorProfile.Unarmored;
            }

            return ArmorProfile.FromCard(card);
        }

        // ───────────────────────────── 敌人 ─────────────────────────────

        private static void LoadEnemies(ContentDatabase db, JsonValue root, string file, LoadReport report)
        {
            foreach (JsonValue node in root["enemies"].Items)
            {
                EnemyContent e = ReadEnemy(node, file, report);
                if (e != null) db.Put(e);
            }
        }

        private static EnemyContent ReadEnemy(JsonValue node, string file, LoadReport report)
        {
            string id = node["id"].AsString();
            if (string.IsNullOrWhiteSpace(id)) { report.Errors.Add($"{file}：有敌人缺少 id"); return null; }

            string where = $"{file} / {id}";
            var errors = report.Errors;

            var affinities = new AffinityTable();
            foreach (KeyValuePair<string, JsonValue> pair in node["affinities"].Fields)
            {
                if (!Enum.TryParse(pair.Key, ignoreCase: true, out DamageElement element))
                {
                    errors.Add($"{where}：affinities 里「{pair.Key}」不是合法的伤害属性");
                    continue;
                }

                affinities.Set(element, pair.Value.AsEnum(
                    Affinity.Normal, $"{where} 的 affinities.{pair.Key}", errors));
            }

            var intents = new Dictionary<string, EnemyIntent>(StringComparer.Ordinal);
            foreach (JsonValue node2 in node["intents"].Items)
            {
                string intentId = node2["id"].AsString();
                if (string.IsNullOrWhiteSpace(intentId)) { errors.Add($"{where}：有意图缺少 id"); continue; }

                intents[intentId] = new EnemyIntent(
                    node2["name"].AsString(intentId),
                    node2["targeting"].AsEnum(
                        IntentTargeting.DefaultSingle, $"{where} 的意图 {intentId} 的 targeting", errors))
                {
                    Damage = node2["damage"].AsInt(),
                    Element = node2["element"].AsEnum(
                        DamageElement.Physical, $"{where} 的意图 {intentId} 的 element", errors),
                    DesignatedRow = node2["designatedRow"].AsEnum(
                        Row.Front, $"{where} 的意图 {intentId} 的 designatedRow", errors),
                    // 「可在响应时由相邻队友分摊」——响应窗口里多给玩家一个选项（B-14）
                    Shareable = node2["shareable"].AsBool(),

                    // 不写就按 targeting 推导（单体锁定 / 范围结算时定，裁定 R-24）
                    LocksTargetOverride = node2["locksTarget"].Exists
                        ? node2["locksTarget"].AsBool()
                        : (bool?)null,

                    // 意图挂的那张「敌人的牌」。宣告出来的那段攻击仍然记在 Damage/Element 上
                    // （响应窗口要预览它、时机牌要改它），卡面负责的是**后续效果**：
                    // 吞噬充能、前后排互换、命刻+1 之类
                    Card = ReadEnemyCard(node2, intentId, where, errors),
                };
            }

            JsonValue pools = node["intentPools"];
            List<string> Pool(string key) => pools[key].Items
                .Select(v => v.AsString())
                .Where(s => s != null)
                .Where(s =>
                {
                    if (intents.ContainsKey(s)) return true;
                    errors.Add($"{where}：意图池 {key} 引用了不存在的意图 {s}");
                    return false;
                })
                .ToList();

            int critResist = node["critResist"].AsInt(-1);

            return new EnemyContent
            {
                Id = id,
                DisplayName = node["name"].AsString(id),
                Hp = node["hp"].AsInt(1),
                Rank = node["rank"].AsEnum(EnemyRank.Normal, $"{where} 的 rank", errors),
                CritResist = critResist < 0 ? null : critResist,
                MaxMp = node["mp"].AsInt(),
                Row = node["row"].AsEnum(Row.Front, $"{where} 的 row", errors),
                Affinities = affinities,
                Intents = intents,
                PhaseOne = Pool("phaseOne"),
                PhaseTwo = Pool("phaseTwo"),
                PhaseTwoHpThreshold = node["phaseTwoHpThreshold"].AsInt(node["hp"].AsInt(1) / 2),
                IntentsPerRound = node["intentsPerRound"].AsInt(3),
            };
        }

        // ───────────────────────────── 卡组 ─────────────────────────────

        private static void LoadDecks(ContentDatabase db, JsonValue root, string file, LoadReport report)
        {
            string id = root["id"].AsString();
            if (string.IsNullOrWhiteSpace(id)) { report.Errors.Add($"{file}：卡组缺少 id"); return; }

            db.Put(new DeckContent
            {
                Id = id,
                DisplayName = root["name"].AsString(id),
                CharacterId = root["characterId"].AsString(),
                Main = ReadEntries(root["main"]),
                FormDecks = root["formDecks"].Items.Select(f => new FormDeckContent
                {
                    TransformCardId = f["transformCard"].AsString(),
                    Cards = ReadEntries(f["cards"]),
                }).ToArray(),
            });
        }

        private static (string, int)[] ReadEntries(JsonValue list)
            => list.Items
                .Select(e => (e["card"].AsString(), Math.Max(1, e["count"].AsInt(1))))
                .Where(e => e.Item1 != null)
                .ToArray();

        private static string Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
    }
}
