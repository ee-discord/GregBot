using System;
using System.Globalization;

namespace ForumCrawler
{
    public class ScoreData
    {
        public double Score { get; set; } = 1;
        public double PermanentScore { get; set; } = 0;
        public double Gems { get; set; }
        public double BoostLevel { get; set; }
        public double BonusScore { get; set; }
        public ulong? AltOfUserId { get; set; }
        public int DailyStreakCount { get; set; }
        public double FinalScore => Score + BonusScore + PermanentScore;
        public int Class => (int)Math.Truncate(ScoreLevel);

        public string ClassString => ToRoman(Class);

        public double ScoreLevel => Math.Truncate(Math.Round(FinalScore * 1000) / 100) / 10;

        public double BaseScoreLevel => Math.Truncate(Math.Round(Score * 1000) / 100) / 10;

        public string ShortBaseScoreString => string.Format(CultureInfo.InvariantCulture, "{0:F1}", BaseScoreLevel);

        public string ShortScoreString => string.Format(CultureInfo.InvariantCulture, "{0:F1}", ScoreLevel);

        public static string ToRoman(int number)
        {
            if ((number < 0) || (number > 3999)) throw new ArgumentOutOfRangeException("insert value between 0 and 3999");
            if (number == 0) return "0";
            return ToRomanInner(number);
        }

        private static string ToRomanInner(int number)
        {
            if (number == 0) return "";
            if (number >= 1000) return "M" + ToRomanInner(number - 1000);
            if (number >= 900) return "CM" + ToRomanInner(number - 900);
            if (number >= 500) return "D" + ToRomanInner(number - 500);
            if (number >= 400) return "CD" + ToRomanInner(number - 400);
            if (number >= 100) return "C" + ToRomanInner(number - 100);
            if (number >= 90) return "XC" + ToRomanInner(number - 90);
            if (number >= 50) return "L" + ToRomanInner(number - 50);
            if (number >= 40) return "XL" + ToRomanInner(number - 40);
            if (number >= 10) return "X" + ToRomanInner(number - 10);
            if (number >= 9) return "IX" + ToRomanInner(number - 9);
            if (number >= 5) return "V" + ToRomanInner(number - 5);
            if (number >= 4) return "IV" + ToRomanInner(number - 4);
            if (number >= 1) return "I" + ToRomanInner(number - 1);
            throw new ArgumentOutOfRangeException("Could not convert to roman");
        }
    }
}