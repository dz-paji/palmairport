using System;

namespace IslandAirport
{
    /// <summary>
    /// 年龄准入（M2=13 岁）。接收 now 参数，本身不取时间（计时源铁律的唯一例外点，
    /// 因为 DOB 比较本质是日历运算）。规则：生日当天满 minAge 算成年；
    /// 2 月 29 日生日在平年 3 月 1 日视为满岁。
    /// </summary>
    public static class AgeCheck
    {
        public const int DefaultMinAge = 13;

        /// <summary>now 时刻的精确周岁。生日未到当年减一。</summary>
        public static int YearsAt(DateTime dob, DateTime now)
        {
            int years = now.Year - dob.Year;
            if (now.Month < dob.Month || (now.Month == dob.Month && now.Day < dob.Day))
            {
                years--;
            }

            return years;
        }

        /// <summary>是否达到准入年龄（含生日当天）。</summary>
        public static bool Allowed(DateTime dob, DateTime now, int minAge)
        {
            return YearsAt(dob, now) >= minAge;
        }

        /// <summary>默认 13 岁准入。</summary>
        public static bool Allowed(DateTime dob, DateTime now)
        {
            return Allowed(dob, now, DefaultMinAge);
        }
    }
}
