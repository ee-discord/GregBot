using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

namespace ForumCrawler
{
    public static class Score
    {
        public static event Action<ulong, ScoreData> OnUpdate;


        private static async Task<T> WithTargetedScoreCommand<T>(DiscordSocketClient client,
            ulong targetUserId, ulong invokerUserId, Func<ScoreUser, ScoreUser, T> callback)
        {
            using (var context = new DatabaseContext())
            {
                var user1 = await Database.GetOrCreateScoreUserAsync(context, client, targetUserId);
                var user2 = await Database.GetOrCreateScoreUserAsync(context, client, invokerUserId);
                var res = callback(user1, user2);
                await context.SaveChangesAsync();
                return res;
            }
        }

        public static async Task<(ScoreData ScoreData, int Increase, int Bonus)> DailyAsync(DiscordSocketClient client, ulong invokerUserId)
        {
            using (var context = new DatabaseContext())
            {
                var user = await Database.GetOrCreateScoreUserAsync(context, client, invokerUserId);
                var (increase, bonus) = user.Daily();
                await context.SaveChangesAsync();
                return (user.ScoreData, increase, bonus);
            }
        }

        public static async Task<(ScoreData, double, (ScoreData ScoreData, int Amount, int Bonus)?)> UpvoteAsync(DiscordSocketClient client, ulong targetUserId,
            ulong invokerUserId)
        {
            return await WithTargetedScoreCommand(client, targetUserId, invokerUserId, (target, upvoter) =>
            {
                (ScoreData ScoreData, int Amount, int Bonus)? daily = null;
                if (upvoter.Gems < 0 && !upvoter.HasDisabledAutoDaily)
                {
                    try
                    {
                        var (increase, bonus) = upvoter.Daily();
                        daily = (target.ScoreData, increase, bonus);
                    }
                    catch { }
                }

                var change = upvoter.Upvote(target);
                OnUpdate?.Invoke(target.UserId, target.ScoreData);
                return (target.ScoreData, change, daily);
            });
        }

        public static async Task<(ScoreData, double, (ScoreData ScoreData, int Amount, int Bonus)?)> DownvoteAsync(DiscordSocketClient client, ulong targetUserId,
            ulong invokerUserId)
        {
            return await WithTargetedScoreCommand(client, targetUserId, invokerUserId, (target, downvoter) =>
            {
                (ScoreData ScoreData, int Amount, int Bonus)? daily = null;
                if (downvoter.Gems < 0 && !downvoter.HasDisabledAutoDaily)
                {
                    try
                    {
                        var (increase, bonus) = downvoter.Daily();
                        daily = (target.ScoreData, increase, bonus);
                    }
                    catch { }
                }

                var change = downvoter.Downvote(target);
                OnUpdate?.Invoke(target.UserId, target.ScoreData);
                return (target.ScoreData, change, daily);
            });
        }

        internal static async Task<(ScoreData, ScoreData)> SendGems(DiscordSocketClient client, ulong targetUserId,
            ulong invokerUserId, int amount)
        {
            return await WithTargetedScoreCommand(client, targetUserId, invokerUserId,
                (target, sender) =>
                {
                    sender.SendGems(target, amount);
                    return (target.ScoreData, sender.ScoreData);
                });
        }

        private static async Task<ScoreUser> GetScoreUserAsync(DiscordSocketClient client, ulong userId)
        {
            using (var context = new DatabaseContext())
            {
                return await Database.GetOrCreateScoreUserAsync(context, client, userId);
            }
        }

        public static async Task<ScoreData> GetScoreDataAsync(DiscordSocketClient client, ulong userId)
        {
            return (await GetScoreUserAsync(client, userId)).ScoreData;
        }

        public static async Task<List<(ulong Key, DateTime LastBoost)>> GetHistoryAsync(DiscordSocketClient client,
            ulong userId)
        {
            return (await GetScoreUserAsync(client, userId)).Boosts
                .Where(kv => (DateTime.UtcNow - kv.Value).TotalDays < 7)
                .Select(kv => (kv.Key, TimeLeft: kv.Value))
                .OrderByDescending(boost => boost.TimeLeft)
                .ToList();
        }

        public static async Task<List<(ulong Key, TimeSpan TimeLeft)>> GetBoostsAsync(DiscordSocketClient client,
            ulong userId)
        {
            return (await GetScoreUserAsync(client, userId)).GetBoostsLeft()
                .Select(kv => (kv.Key, TimeLeft: kv.Value))
                .OrderByDescending(boost => boost.TimeLeft)
                .ToList();
        }

        public static async Task<List<(ulong Key, TimeSpan TimeLeft)>> GetBoostingsAsync(DiscordSocketClient client,
            ulong userId)
        {
            using (var context = new DatabaseContext())
            {
                return (await Database.GetAllScoreUsersAsync(context, client).ToListAsync())
                    .Select(u => (Key: u.UserId, BoostsLeft: u.GetBoostsLeft()))
                    .Where(u => u.BoostsLeft.ContainsKey(userId))
                    .Select(u => (u.Key, TimeLeft: u.BoostsLeft[userId]))
                    .OrderByDescending(boost => boost.TimeLeft)
                    .ToList();
            }
        }

        public static async Task SwapUsers(DiscordSocketClient client, ulong user1Id, ulong user2Id)
        {
            ScoreUser user1, user2;
            using (var context = new DatabaseContext())
            {
                user1 = await Database.GetOrCreateScoreUserAsync(context, client, user1Id);
                user2 = await Database.GetOrCreateScoreUserAsync(context, client, user2Id);
            }

            ScoreUser.SwapUsers(user1, user2);

            // EF does not let us change the primary key of an object 
            // We create a new context to make it think everything but the ID was swapped
            using (var context = new DatabaseContext())
            {
                context.ScoreUsers.Attach(user1);
                context.ScoreUsers.Attach(user2);
                context.Entry(user1).State = EntityState.Modified;
                context.Entry(user2).State = EntityState.Modified;
                await context.SaveChangesAsync();
            }
        }

        public static async Task<ScoreData> CreditActivityScoreAsync(DiscordSocketClient client, ulong activityUserId)
        {
#if DEBUG
            return await GetScoreDataAsync(client, activityUserId);
#else
            using (var context = new DatabaseContext())
            {
                var user = await Database.GetOrCreateScoreUserAsync(context, client, activityUserId);
                user.CreditActivity();
                await context.SaveChangesAsync();
                OnUpdate?.Invoke(user.UserId, user.ScoreData);
                return user.ScoreData;
            }
#endif
        }

        public static async Task UpdateDecays(DiscordSocketClient client)
        {
            using (var context = new DatabaseContext())
            {
                // this already calls update() on all users
                var users = await Database.GetAllScoreUsersAsync(context, client)
                    .ToListAsync();
                await context.SaveChangesAsync();

                foreach (var user in users)
                {
                    if (user.DidJustFallUnderThreshold && !user.HasDisabledThresholdWarning)
                    {
                             "You can disable this warning by calling `g!thresholdwarning false`");
                    }

                    OnUpdate?.Invoke(user.UserId, user.ScoreData);
                }
            }
        }

        public static async Task MarkAltOf(DiscordSocketClient client, ulong userId, ulong? value)
        {
            using (var context = new DatabaseContext())
            {
                var user = await Database.GetOrCreateScoreUserAsync(context, client, userId);
                user.AltOfUserId = value;
                await context.SaveChangesAsync();
            }
        }

        public static async Task SetHasDisabledThresholdWarning(DiscordSocketClient client, ulong userId, bool disabled)
        {
            using (var context = new DatabaseContext())
            {
                var user = await Database.GetOrCreateScoreUserAsync(context, client, userId);
                user.HasDisabledThresholdWarning = disabled;
                await context.SaveChangesAsync();
            }
        }

        public static async Task SetHasDisabledAutoDaily(DiscordSocketClient client, ulong userId, bool disabled)
        {
            using (var context = new DatabaseContext())
            {
                var user = await Database.GetOrCreateScoreUserAsync(context, client, userId);
                user.HasDisabledAutoDaily = disabled;
                await context.SaveChangesAsync();
            }
        }
    }
}