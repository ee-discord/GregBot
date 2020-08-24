using Discord;
using Discord.WebSocket;
using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace ForumCrawler
{
    public class ScoreUser
    {

        private static readonly Random Random = new Random();


        private const double ScorePointMultiplier = 0.0015;
        private const double ScoreEpsilon = 0.5;
        private const double MaxScore = 5;

        private const double ActivityResolutionInHours = 60.0 / 3600; // 60 seconds


        private const double Level5Decay = 32; // Math.Pow(2, 5)

        private const double ScoreBaseDecayRate = 0.001;
        private const double ScoreInactivityDecayRate = 0.0001;

        [EditorBrowsable(EditorBrowsableState.Never),
            DatabaseGenerated(DatabaseGeneratedOption.None)]
        public long Id { get; set; }

        [NotMapped]
        public ulong UserId
        {
            get => (ulong)Id;
            set => Id = (long)value;
        }

        public DateTime? LastActivity { get; set; }
        public DateTime? LastDecay { get; set; }
        public DateTime? LastDaily { get; set; }
        public int Gems { get; set; }

        [Index]
        public double Score { get; set; } = 1;

        [NotMapped]
        public Dictionary<ulong, DateTime> Boosts { get; private set; } = new Dictionary<ulong, DateTime>();

        public string BoostsAsString
        {
            get => JsonConvert.SerializeObject(Boosts);
            set => Boosts = JsonConvert.DeserializeObject<Dictionary<ulong, DateTime>>(value ?? "{}");
        }

        [NotMapped]
        public Dictionary<ulong, DateTime> DownBoosts { get; private set; } = new Dictionary<ulong, DateTime>();

        public string DownBoostsAsString
        {
            get => JsonConvert.SerializeObject(DownBoosts);
            set => DownBoosts = JsonConvert.DeserializeObject<Dictionary<ulong, DateTime>>(value ?? "{}");
        }

        [NotMapped]
        private double ScorePoints
        {
            get => ToPoints(Score, MaxScore, ScoreEpsilon, ScorePointMultiplier); // ln((1 - x / 5.1) / -0.0015
            set => Score = ToValue(value, MaxScore, ScoreEpsilon, ScorePointMultiplier); // 5.1 * (1 - e^(-0.0015x))
        }

        [NotMapped]
        {
        }

        private static double ToPoints(double value, double max, double epsilon, double multiplier) => Math.Log(1 - value / (max + epsilon)) / -multiplier;

        private static double ToValue(double points, double max, double epsilon, double multiplier) => Math.Max(0, Math.Min(max, (max + epsilon) * (1 - Math.Exp(-multiplier * points))));


        [NotMapped]
        public double BonusScore
        {
            get
            {
                var boostLevel = GetBoostLevel();
                return (IsPremium ? 1 : 0) +
                    (boostLevel >= 3
                    ? 1.0
                    : boostLevel >= 2
                    ? 0.4
                    : boostLevel >= 1
                    ? 0.1
                    : 0);
            }
        }

        [NotMapped]
        public ScoreData ScoreData
        {
            get
            {
                return new ScoreData
                {
                    Score = Score,
                    Gems = Gems,
                    BoostLevel = GetBoostLevel(),
                    BonusScore = BonusScore,
                    AltOfUserId = AltOfUserId,
                    DailyStreakCount = DailyStreakCount
                };
            }
        }

        [NotMapped]
        public bool DidJustFallUnderThreshold { get; private set; }

        [NotMapped]
        public bool IsPremium { get; private set; }

        private int GetBoostLevel()
        {
            var boosts = GetBoostsLeft().Count;
            return boosts >= 6
                ? 3
                : boosts >= 3
                ? 2
                : boosts >= 1
                ? 1
                : 0;
        }

        public Dictionary<ulong, TimeSpan> GetBoostsLeft()
        {
            return Boosts
                .Select(kv => new KeyValuePair<ulong, TimeSpan>(kv.Key, TimeSpan.FromDays(2) - (DateTime.UtcNow - kv.Value)))
                .Where(boost => boost.Value.TotalSeconds > 0)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        public DateTime GetLastVoteTimestamp(ulong userId)
        {
            Boosts.TryGetValue(userId, out var boostDate);
            DownBoosts.TryGetValue(userId, out var downVoteDate);
            return boostDate > downVoteDate ? boostDate : downVoteDate;
        }

        public void Update(DiscordSocketClient client)
        {
            var guild = client.GetGuild(DiscordSettings.GuildId);
            var guildUser = guild.GetUser(this.UserId);
            if (guildUser != null)
            {
                IsPremium = guildUser.PremiumSince.HasValue;
            }

            UpdateDecay();
        }

        private double SumInRange(double minExl, double maxInc)
        {
           return (maxInc * (maxInc + 1) - minExl * (minExl + 1)) / 2;
        }

        internal static void SwapUsers(ScoreUser user1, ScoreUser user2)
        {
            var user1Id = user1.UserId;
            user1.UserId = user2.UserId;
            user2.UserId = user1Id;
        }

        private void UpdateDecay()
        {
            var lastDecay = LastDecay ?? DateTime.UtcNow;
            var lastActivity = LastActivity ?? default;
            var ticks = (DateTime.UtcNow - lastDecay).TotalHours;
            var lastActivityTicks = (DateTime.UtcNow - lastActivity).TotalHours;


            {
                this.DidJustFallUnderThreshold = true;
            }

            if (Score > 2)
            { 
                this.Score -= SumInRange(lastActivityTicks - remainderTicks, lastActivityTicks) * ScoreInactivityDecayRate 
                    + ScoreBaseDecayRate * remainderTicks;
                if (this.Score < 2) this.Score = 2;
            }

            LastDecay = DateTime.UtcNow;
        }

        private bool TickActivity()
        {
            var resolution = TimeSpan.FromHours(ActivityResolutionInHours);
            var delta = DateTime.UtcNow.Subtract(this.LastActivity ?? DateTime.UtcNow.Subtract(resolution + resolution));
            if (delta < resolution)
            {
                return false;
            } 
            else if (delta < resolution + resolution)
            {
                this.LastActivity += resolution;
            } 
            else
            {
                this.LastActivity = DateTime.UtcNow;
            }

            return true;
        }

        public bool CreditActivity()
        {
            if (this.TickActivity())
            {
                if (remainder > 0)
                {
                    this.ScorePoints += 1;
                }
                return true;
            }
            return false;
        }

        public double Upvote(ScoreUser target)
        {
            var efficiency = GetEfficiency(target);
            if (this.Gems < 1) throw new Exception($"An upvote costs 1 gem, which you currently do not have.");
            this.Gems -= 1;

            var lowScoreFactor = target.Score < 2 ? 3 : target.Score < 3 ? 2 : 1;
            target.ScorePoints += 15 * efficiency * lowScoreFactor;
            target.Boosts[this.UserId] = DateTime.UtcNow;

            return efficiency;
        }

        public double Downvote(ScoreUser target)
        {
            var efficiency = GetEfficiency(target);
            if (this.Gems < 1) throw new Exception($"A downvote costs 1 gem, which you currently do not have.");
            this.Gems -= 1;
            target.ScorePoints -= 15 * efficiency;
            target.DownBoosts[this.UserId] = DateTime.UtcNow;

            return efficiency;
        }

        [NotMapped]
        public TimeSpan? DailyCooldown
        {
            get
            {
                var daysSinceLastDaily = DateTime.UtcNow - (this.LastDaily ?? default);
                var cooldown = TimeSpan.FromDays(1) - daysSinceLastDaily;
                if (cooldown.TotalSeconds <= 0)
                    return null;
                return cooldown;
            }
        }

        [NotMapped]
        public bool CanStreak
        {
            get
            {
                var daysSinceLastDaily = DateTime.UtcNow - (this.LastDaily ?? default);
                var cooldown = TimeSpan.FromDays(2) - daysSinceLastDaily;
                return cooldown.TotalSeconds >= 0;
            }
        }

        public long? AltOfId { get; set; }

        [NotMapped]
        public ulong? AltOfUserId
        {
            get => (ulong?)AltOfId;
            set => AltOfId = (long?)value;
        }

        public int DailyCount { get; set; }
        public int DailyStreakCount { get; set; }
        public bool HasDisabledThresholdWarning { get; set; }
        public bool HasDisabledAutoDaily { get; set; }

        public (int Amount, int Bonus) Daily()
        {
            if (this.AltOfUserId.HasValue)
                throw new Exception("Sorry, alts cannot receive gems.");

            if (this.DailyCooldown.HasValue) 
                throw new Exception($"You have already used your daily today. Come back in {this.DailyCooldown.Value.ToHumanReadableString()}.");

                throw new Exception($"You've used your 3 trial g!daily commands. " +
                                    $"Daily gems are available to active members only. " +
            
            var amount = 3;

            var bonus = 0;
            if (CanStreak)
            {
                this.DailyStreakCount++;

                if (DailyStreakCount % 7 == 0)
                {
                    bonus = 3;
                }
            }
            else
            {
                this.DailyStreakCount = 0;
            }

            this.DailyCount++;
            this.LastDaily = DateTime.UtcNow.Date;
            this.Gems += amount + bonus;

            return (amount, bonus);
        }

        internal void SendGems(ScoreUser target, int amount)
        {
            if (this.AltOfUserId.HasValue || target.AltOfUserId.HasValue)
                throw new Exception("Sorry, alts cannot send or receive gems.");

            DeductGems(amount);
            target.Gems += amount;
        }

        private void DeductGems(int amount)
        {
            if (amount < 1) throw new Exception("Amount cannot be smaller than 1.");
            if (this.Gems < amount)
                throw new Exception("You don't have that many gems.");
            this.Gems -= amount;
        }

        private double GetEfficiency(ScoreUser target)
        {
            if (this.AltOfUserId.HasValue || target.AltOfUserId.HasValue)
                throw new Exception("Sorry, alts cannot send or receive votes.");
            if (target.UserId == UserId) throw new Exception($"Sorry, voting yourself is not allowed!");

            var lastBoost = target.GetLastVoteTimestamp(this.UserId);
            var sinceLastVote = DateTime.UtcNow - lastBoost;
            var cooldown = TimeSpan.FromDays(0.5) - sinceLastVote;
            if (cooldown.TotalSeconds > 0) throw new Exception($"Please wait {cooldown.ToHumanReadableString()} before voting this person again.");

            var randomEff = Math.Max(0.75, Random.NextDouble() * 2);
            var discountFactor = 0.5 + 0.5 * (Math.Min(2, sinceLastVote.TotalDays) / 2);

            var efficiency = discountFactor * randomEff;
            return efficiency;
        }
    }
}