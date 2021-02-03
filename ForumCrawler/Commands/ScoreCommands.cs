using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace ForumCrawler
{
    [Group]
    public class ScoreCommands : ModuleBase<SocketCommandContext>
    {
        [Command("help")]
        [RequireChannel(DiscordSettings.BotCommandsChannel)]
        public async Task GetHelp()
        {
            await ReplyAsync("Help is here! <https://gist.github.com/Yonom/126e7b6741f4e865c8b41e9488cf578a>");
        }

        [Command("history")]
        [RequireChannel(DiscordSettings.BotCommandsChannel)]
        [Priority(0)]
        public async Task GetMyHistory()
        {
            await GetHistory(Context.User as IGuildUser);
        }

        [Command("history")]
        [RequireChannel(DiscordSettings.BotCommandsChannel)]
        [Priority(1)]
        public async Task GetHistory(IGuildUser user)
        {
            var history = await Score.GetHistoryAsync(Context.Client, user.Id);
            var historyString = history.Count == 0
                ? " nobody."
                : history.Count == 1
                    ? $" {GetHistoryAsync(history[0])}."
                    : ": ```yml\n" +
                      string.Join("\n", history.Select(a => $"- {GetHistoryAsync(a)}")) + "\n" +
                      "```";
            await ReplyAsync($"{user.GetName()} has been boosted by{historyString}");
        }

        [Command("boosts")]
        [RequireChannel(DiscordSettings.BotCommandsChannel)]
        [Priority(0)]
        public async Task GetMyBoosts()
        {
            await GetBoosts(Context.User as IGuildUser);
        }

        [Command("boosts")]
        [RequireChannel(DiscordSettings.BotCommandsChannel)]
        [Priority(1)]
        public async Task GetBoosts(IGuildUser user)
        {
            var boosts = await Score.GetBoostsAsync(Context.Client, user.Id);
            var boostString = boosts.Count == 0
                ? " nobody."
                : boosts.Count == 1
                    ? $" {GetBoostString(boosts[0])}."
                    : ": ```yml\n" +
                      string.Join("\n", boosts.Select(a => $"- {GetBoostString(a)}")) + "\n" +
                      "```";
            await ReplyAsync($"{user.GetName()} is being boosted by{boostString}");
        }

        [Command("boosting")]
        [RequireChannel(DiscordSettings.BotCommandsChannel)]
        [Priority(0)]
        public async Task GetMyBoosting()
        {
            await GetBoosting(Context.User as IGuildUser);
        }

        [Command("boosting")]
        [RequireChannel(DiscordSettings.BotCommandsChannel)]
        [Priority(1)]
        public async Task GetBoosting(IGuildUser user)
        {
            var boostings = await Score.GetBoostingsAsync(Context.Client, user.Id);
            var boostingString = boostings.Count == 0
                ? " nobody."
                : boostings.Count == 1
                    ? $" {GetBoostString(boostings[0])}."
                    : ": ```yml\n" +
                      string.Join("\n", boostings.Select(a => $"- {GetBoostString(a)}")) + "\n" +
                      "```";
            await ReplyAsync($"{user.GetName()} is boosting{boostingString}");
        }

        [Command("stats")]
        [Alias("score")]
        [Priority(0)]
        [RequireChannel(DiscordSettings.BotCommandsChannel)]
        public async Task GetMyStats()
        {
            await GetStats(Context.User);
        }

        [Command("stats")]
        [Alias("score")]
        [Priority(1)]
        [RequireChannel(DiscordSettings.BotCommandsChannel)]
        public async Task GetStats(IUser userObj)
        {
            var user = Context.Client.GetGuild(DiscordSettings.GuildId).GetUser(userObj.Id);
            var (score, position) = await Database.UNSAFE_GetOrCreateScoreUserAndLeaderboardPositionAsync(Context.Client, user.Id);

            var headerStr = $"{GetMedalStr(position)} **{user.GetName()}**'s stats:";

            const string WootStr = "<:woot:329697747701727235>";
            Emote WootEmote = Emote.Parse(WootStr);
            var boostStr = score.BonusScore > 0 ? $" (+{score.BonusScore:F1})" : "";

            var dailyStr = score.DailyCooldown.HasValue 
                ? score.DailyStreakCount == 0 ? "" 
                : $" (Streak: {score.DailyStreakCount})" 
                : " (Daily available)";

            var permaScoreStr = score.PermanentScore > 0
                ? $":medal: **Daily:** {score.DailyCount} (PermaBoost: +{score.PermanentScore})\n"
                : "";

            await ReplyAsync(headerStr, embed: new EmbedBuilder().WithDescription(
                WootEmote + $" **Score:** {score.Score:F3}{boostStr}\n" +
                permaScoreStr +
                $":gem: **Gems:** {score.Gems}{dailyStr}\n" +
                $":rocket: **Inertia:** {score.Inertia * 100:F0}%"
            ).Build());
        }

        private string GetMedalStr(int scorePosition) {
            return scorePosition switch {
                1 => ":first_place:",
                2 => ":second_place:",
                3 => ":third_place:",
                _ => $"[#{scorePosition}]"
            };
        }

        [Command("transfer")]
        [RequireRole(DiscordSettings.DiscordServerOwner)]
        public async Task Transfer(IUser user1, IUser user2)
        {
            await Score.SwapUsers(Context.Client, user1.Id, user2.Id);
            await ReplyAsync(
                $"Swapped {MentionUtils.MentionUser(user1.Id)}'s user data with {MentionUtils.MentionUser(user2.Id)}'s.");
        }

        [Command("daily")]
        [RequireChannel(DiscordSettings.BotCommandsChannel)]
        public async Task Daily()
        {
            var (scoreData, amount, bonus) = await Score.DailyAsync(Context.Client, Context.User.Id);
            await ReplyAsync(GetDailyStr(scoreData, amount, bonus));
        }

        private string GetDailyStr(ScoreData scoreData, int amount, int bonus)
        {
            var bonusStr = bonus == 0 ? "" : $" (+3 streak bonus)";
            var streakStr = scoreData.DailyStreakCount == 0 ? "" : $" Streak days: {scoreData.DailyStreakCount}\n";

            return $"{MentionUtils.MentionUser(Context.User.Id)} collected their {amount}{bonusStr} daily gems. " +
                $"They now have {scoreData.Gems} in total.\n" +
                streakStr +
                $"Permanent score: {scoreData.PermanentScore} (+0.003)";
        }

        [Command("up")]
        [RequireChannel(DiscordSettings.BotCommandsChannel)]
        public async Task UpUser(IGupUser gupUser)
        {
            var user = gupUser.ActualUser;

            if (!(user is IGuildUser targetUser))
            {
                throw new ArgumentException("user isn't IGupUser");
			}

            var oldScoreData = await Score.GetScoreDataAsync(Context.Client, targetUser.Id);
            var (scoreData, efficiency, daily) = await Score.UpvoteAsync(Context.Client, targetUser.Id, Context.User.Id);

            if (daily.HasValue)
            {
                await ReplyAsync(GetDailyStr(daily.Value.ScoreData, daily.Value.Amount, daily.Value.Bonus) +
                                 " (g!daily was automatically called. To disable this feature, invoke `g!autodaily false`)");
            }

            await ReplyAsync(
                $"{MentionUtils.MentionUser(Context.User.Id)} gave {MentionUtils.MentionUser(targetUser.Id)} a boost. Their score increased by {scoreData.Score - oldScoreData.Score:F3} (Efficiency: {efficiency * 100:F0}%).");

            if (scoreData.BonusScore != oldScoreData.BonusScore)
            {
                await ReplyAsync(
                    $"{MentionUtils.MentionUser(targetUser.Id)} reached boost level {scoreData.BoostLevel}! +{scoreData.BonusScore:F1} temporary bonus score.");
            }
        }

        #if DEBUG
        [Command("pu")]
        public async Task PuUser(IGupUser targetGupUser)
        {
            var user = targetGupUser.ActualUser;

            await ReplyAsync("Called PuUser with: " + user.Username + ", " + user.Mention + ", " + user.Id);
		}
        #endif

        [Command("down")]
        [RequireChannel(DiscordSettings.BotCommandsChannel)]
        public async Task DownUser(IGuildUser targetUser)
        {
            var oldScoreData = await Score.GetScoreDataAsync(Context.Client, targetUser.Id);
            var (scoreData, efficiency, daily) = await Score.DownvoteAsync(Context.Client, targetUser.Id, Context.User.Id);

            if (daily.HasValue)
            {
                await ReplyAsync(GetDailyStr(daily.Value.ScoreData, daily.Value.Amount, daily.Value.Bonus) +
                                 " (g!daily was automatically called. To disable this feature, invoke `g!autodaily false`)");
            }

            await ReplyAsync(
                $"{MentionUtils.MentionUser(Context.User.Id)} gave {MentionUtils.MentionUser(targetUser.Id)} a downvote. Their score decreased by {oldScoreData.Score - scoreData.Score:F3} (Efficiency: {efficiency * 100:F0}%).");
        }

        [Command("send gem")]
        [RequireChannel(DiscordSettings.BotCommandsChannel)]
        public async Task SendGems(IGuildUser targetUser, int amount)
        {
            var (targetData, senderData) = await Score.SendGems(Context.Client, targetUser.Id, Context.User.Id, amount);

            await ReplyAsync(
                $"{MentionUtils.MentionUser(Context.User.Id)} sent {MentionUtils.MentionUser(targetUser.Id)} {amount} gems. " +
                $"They now have {targetData.Gems} in total. You now have {senderData.Gems} gems.");
        }

        [Command("top")]
        [RequireChannel(DiscordSettings.BotCommandsChannel)]
        public async Task Top(int page = 1)
        {
            var topPlayers = await Database.UNSAFE_GetScoreUsersByLeaderboardPositionAsync(page - 1);
            var myPlayer =
                await Database.UNSAFE_GetOrCreateScoreUserAndLeaderboardPositionAsync(Context.Client, Context.User.Id);

            var topPlayerStrings = topPlayers.Select(t =>
                GetLeaderboardPlayerString(t, (10 * (page - 1)) + 1 + topPlayers.Count(t2 => t2.Score > t.Score)));
            var myPlayerString = GetLeaderboardPlayerString(myPlayer.Item1, myPlayer.Item2);

            await ReplyAsync("```py\n" +
                             "Pos  Username                          Score\n" +
                             "--------------------------------------------\n" +
                             string.Join("\n", topPlayerStrings) + "\n" +
                             "--------------------------------------------\n" +
                             myPlayerString +
                             "```");
        }

        [Command("confirm")]
        [RequireChannel(DiscordSettings.UnverifiedChannel)]
        [Priority(1)]
        public async Task Confirm()
        {
            await Confirm((SocketGuildUser)Context.User);
        }

        [Command("confirm")]
        [RequireRole(DiscordSettings.DiscordStaff)]
        [Priority(0)]
        public async Task Confirm(SocketGuildUser user)
        {
            await SocialScoreWatcher.UpdateUserAsync(Context.Client, user,
                await Score.GetScoreDataAsync(Context.Client, user.Id), true);
        }

        [Command("thresholdwarning")]
        // [RequireRole(DiscordSettings.DiscordStaff)]
        [Priority(0)]
        public async Task ThresholdWarning(bool enabled)
        {
            await Score.SetHasDisabledThresholdWarning(Context.Client, Context.User.Id, !enabled);
            await ReplyAsync($"Set threshold warnings to {enabled}. Threshold warnings warn you when your inertia drops below 10%.");
        }

        [Command("autodaily")]
        [Priority(0)]
        public async Task AutoDaily(bool enabled)
        {
            await Score.SetHasDisabledAutoDaily(Context.Client, Context.User.Id, !enabled);
            await ReplyAsync($"Set auto daily to {enabled}. Auto daily automatically calls g!daily when you try to call a gem command and do not possess the necessary gems.");
        }

        [Command("alt set")]
        [RequireRole(DiscordSettings.DiscordStaff)]
        [Priority(0)]
        public async Task Alt(IUser user, IUser user2)
        {
            await Score.MarkAltOf(Context.Client, user.Id, user2.Id);
            await ReplyAsync($"Marked {user.Mention} as an alt of {user2.Mention}.");
        }

        [Command("alt")]
        [RequireRole(DiscordSettings.DiscordStaff)]
        [Priority(1)]
        public async Task Alt(IUser user)
        {
            var scoreData = await Score.GetScoreDataAsync(Context.Client, user.Id);
            await ReplyAsync($"{user.Mention} is an alt of {(scoreData.AltOfUserId.HasValue ? MentionUtils.MentionUser(scoreData.AltOfUserId.Value) : "nobody")}.");
        }

        [Command("alt unset")]
        [RequireRole(DiscordSettings.DiscordStaff)]
        [Priority(0)]
        public async Task UnAlt(IUser user)
        {
            await Score.MarkAltOf(Context.Client, user.Id, null);
            await ReplyAsync($"Marked {user.Mention} as an alt of nobody.");
        }

        private string GetHistoryAsync((ulong Key, DateTimeOffset LastBoost) user)
        {
            return
                $"{Context.Client.GetGuild(DiscordSettings.GuildId).GetUser(user.Key)?.GetName() ?? $"<{user.Key}>"} ({(DateTimeOffset.UtcNow - user.LastBoost).ToHumanReadableString()} ago)";
        }

        private string GetBoostString((ulong Key, TimeSpan TimeLeft) user)
        {
            return
                $"{Context.Client.GetGuild(DiscordSettings.GuildId).GetUser(user.Key)?.GetName() ?? $"<{user.Key}>"} ({user.TimeLeft.ToHumanReadableString()} left)";
        }

        private string GetLeaderboardPlayerString(ScoreUser scoreUser, int position)
        {
            var user = Context.Client.GetGuild(DiscordSettings.GuildId).GetUser(scoreUser.UserId);
            var username = user?.GetName() ?? $"<{scoreUser.UserId}>";
            var positionStr = position == 0 ? "" : position.ToString();
            return $"{positionStr,3}  {username,32}  {scoreUser.Score:F3}";
        }
    }
}
