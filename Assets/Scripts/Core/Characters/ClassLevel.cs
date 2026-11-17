using System;

namespace TCGinFU.Core
{
    /// <summary>角色在某个职业上投入的等级。单职业上限 10 级，满 10 即精通（规则书 §2.1 / §3.3）。</summary>
    public readonly struct ClassLevel : IEquatable<ClassLevel>
    {
        public const int MaxPerClass = 10;

        public string ClassId { get; }
        public int Level { get; }

        public ClassLevel(string classId, int level)
        {
            if (string.IsNullOrWhiteSpace(classId))
            {
                throw new ArgumentException("职业 Id 不能为空", nameof(classId));
            }

            if (level < 1 || level > MaxPerClass)
            {
                throw new ArgumentOutOfRangeException(nameof(level), level, $"职业等级须在 1..{MaxPerClass} 之间");
            }

            ClassId = classId;
            Level = level;
        }

        /// <summary>是否已精通该职业（可获得 1 张英雄技能牌）。</summary>
        public bool IsMastered => Level >= MaxPerClass;

        public bool Equals(ClassLevel other) => ClassId == other.ClassId && Level == other.Level;
        public override bool Equals(object obj) => obj is ClassLevel other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(ClassId, Level);
        public override string ToString() => $"{ClassId} {Level} 级";
    }
}
