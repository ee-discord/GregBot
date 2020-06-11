using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Discord;
using Discord.Addons.Interactive;
using Discord.Commands;
using Discord.WebSocket;
using DiscordSocialScore;

namespace ForumCrawler.Commands
{
    public class RoleCommands : ModuleBase<SocketCommandContext>
    {
        [Command("nick"), RequireChannel(DiscordSettings.BotCommandsChannel)]
        public async Task ChangeNickname([Remainder] string nick)
        {
            var user = (IGuildUser)this.Context.User;
            var score = await Score.GetScoreDataAsync(user);
            if (score.ScoreLevel < 4) throw new Exception("You must be a class of 4 or higher to change your nick.");
            await user.ModifyAsync(u => u.Nickname = nick);
            await this.ReplyAsync("Your nickname was updated.");
        }
    }

    public class MuteCommands : ModuleBase<SocketCommandContext>
    {
        private string GetUnmentionedUser(ulong userId)
        {
            var user = this.Context.Guild.GetUser(userId);
            return user.Username.DiscordEscape() + "#" + user.Discriminator;
        }

        public Task<Mute> MuteUser(IUser issuer, IUser user, DateTimeOffset expiry, string reason, bool shorten, bool sameAuthorShorten)
        {
            return MuteWatcher.MuteUser(new Mute
            {
                UserId = user.Id,
                IssuerId = issuer.Id,
                ExpiryDate = expiry.UtcDateTime,
                IssueDate = DateTime.UtcNow
            }, reason, shorten, sameAuthorShorten);
        }

        public async Task UnmuteUser(IUser user, bool force)
        {
            var mute = await Database.GetMute(user.Id);
            if (mute == null)
                throw new Exception("User is currently not muted.");
            if (mute.IssuerId != this.Context.User.Id && !force)
                throw new Exception("Mute can only be removed by its issuer, " + MentionUtils.MentionUser(mute.IssuerId) + ".");
            var state = WarningState.FromDatabase(await Database.GetWarningsAsync(user.Id));
            if (!state.MutedUntil.HasValue)
            {
                await MuteWatcher.UnmuteUser(user.Id, null);
                await this.ReplyAsync($"{MentionUtils.MentionUser(user.Id)} unmuted.");
            }
            else if (state.MutedUntil.Value < mute.ExpiryDate)
            {
                var res = await MuteUser(this.Context.Client.CurrentUser, user, state.MutedUntil.Value, $"Mute shortened.", true, false);
                await this.ReplyAsync($"{MentionUtils.MentionUser(user.Id)} muted until {res.ExpiryDate}. " +
                    $"A reduction of the mute duration beyond this point is not possible due to an active auto-mute from the warnings system.");
            }
            else
            {
                await this.ReplyAsync($"{MentionUtils.MentionUser(user.Id)} muted until {mute.ExpiryDate}. " +
                    $"A reduction of the mute duration beyond this point is not possible due to an active auto-mute from the warnings system.");
            }
        }

        [Command("mute"), RequireRole(DiscordSettings.DiscordStaff)]
        public async Task Mute(IGuildUser user, TimeSpan duration, [Remainder] string reason)
        {
            var mute = await Database.GetMute(user.Id);
            if (mute != null)
                throw new Exception("User is already muted.");
            if (user.RoleIds.Contains(DiscordSettings.DiscordStaff))
                throw new Exception("User is staff.");

            var res = await MuteUser(this.Context.User, user, DateTimeOffset.UtcNow + duration, reason, false, false);
            await this.ReplyAsync($"{MentionUtils.MentionUser(user.Id)} muted until {res.ExpiryDate}.");
        }

        [Command("force unmute"), RequireRole(DiscordSettings.DiscordStaff)]
        public async Task ForceUnmute(IUser user)
        {
            await UnmuteUser(user, true);
        }

        [Command("unmute"), RequireRole(DiscordSettings.DiscordStaff)]
        public async Task Unmute(IUser user)
        {
            await UnmuteUser(user, false);
        }

        [Command("mute status"), Alias("mute stats"), RequireChannel(DiscordSettings.BotCommandsChannel)]
        public async Task MuteState(IUser user = null)
        {
            if (user == null) user = this.Context.User;
            var mute = await Database.GetMute(user.Id);
            await this.ReplyAsync($"{this.GetUnmentionedUser(user.Id)} muted until {mute.ExpiryDate} UTC.");
        }
    }

    [Group("warn"), Alias("warning", "warns", "warnings")]
    public class WarningCommands : InteractiveBase<SocketCommandContext>
    {
        private EmbedBuilder AddWarningsToEmbed(EmbedBuilder embed, WarningState state)
        {
            if (state.Warnings > 0)
            {
                embed.AddField($"Warnings ({state.Warnings})", string.Concat(Enumerable.Repeat("⚠️ ", state.Warnings)), true);
                embed.WithColor(Color.Orange);
            }
            if (state.Strikes > 0)
            {
                embed.AddField($"Strikes ({state.Strikes})", string.Concat(Enumerable.Repeat("❌ ", state.Strikes)), true);
                embed.WithColor(Color.Red);
            }
            if (state.Warnings == 0 && state.Strikes == 0)
            {
                embed.AddField("Warnings (0)", "No warnings, well done!");
                embed.WithColor(Color.Green);
            }
            else if (state.MutedUntil.HasValue)
            {
                embed.AddField($"Strike mute penalty expires in", (state.MutedUntil.Value - DateTimeOffset.UtcNow).ToHumanReadableString());
            }
            else
            {
                var next = state.Warnings == 0 ? "strike" : "warning";
                var duration = state.Warnings == 0
                    ? TimeSpan.FromDays(WarningState.WarningDelayLogicV2.StrikeExpiryDays)
                    : TimeSpan.FromDays(WarningState.WarningDelayLogicV2.WarningExpiryDays);
                embed.AddField($"Next {next} expires in", (duration - (DateTimeOffset.UtcNow - state.LastTick)).ToHumanReadableString());
            }

            return embed;
        }

        private EmbedBuilder GetWarningEmbed(IUser user, Warning warning)
        {
            return new EmbedBuilder()
                .WithAuthor(user)
                .WithColor(Color.Gold)
                .AddField("User", MentionUtils.MentionUser(warning.UserId), true)
                .AddField("Moderator", MentionUtils.MentionUser(warning.IssuerId), true)
                .AddField("Channel", MentionUtils.MentionChannel(warning.ChannelId), true)
                .AddField("Reason", warning.Reason)
                .WithFooter("ID: " + warning.Id)
                .WithCurrentTimestamp();
        }

        private EmbedBuilder GetWarningIssuedEmbed(IUser user, Warning warning, WarningState state)
        {
            return (warning.Amount == 0 ? GetWarningEmbed(user, warning) : AddWarningsToEmbed(GetWarningEmbed(user, warning), state))
                            .WithTitle($"**{(warning.Type)}**");
        }

        private async Task EditWarningInternalAsync(IUser user, long id, string reason, bool force)
        {
            await Database.WithWarningsAsync(user.Id, async history =>
            {
                var warning = history.FirstOrDefault(w => w.Id == id);
                if (warning == null)
                    throw new Exception("Invalid warning ID.");
                if (warning.UserId != user.Id) // this check exists to catch typos in warning IDs
                    throw new Exception("Warning ID does not match with user!");
                if (warning.IssuerId != Context.User.Id && !force)
                    throw new Exception("Warning can only be edited by its issuer, " + MentionUtils.MentionUser(warning.IssuerId) + ".");

                var oldReason = warning.Reason;
                warning.Reason = reason ?? warning.Reason;
                
                var tempEmbed = GetWarningEmbed(user, warning)
                    .WithTitle("**A warning is being edited...**")
                    .WithColor(Color.Blue)
                    .Build();

                var oldSeverity = warning.Type;
                var state = await ApplyWarningWithSeverity(user, history, warning, tempEmbed);
                if (warning.Type != oldSeverity || warning.Reason != oldReason)
                {
                    var embed = GetWarningIssuedEmbed(user, warning, state);
                    if (warning.Type != oldSeverity)
                    {
                        if (warning.Amount == 0) AddWarningsToEmbed(embed, state);
                        embed.Title += $" (Changed from {oldSeverity})";
                    }
                    if (warning.Reason != oldReason)
                    {
                        embed.Title += $" (Reason updated)";
                        embed.Fields.Insert(3, new EmbedFieldBuilder().WithName("Old Reason").WithValue(oldReason));
                    }
                    await AnnounceWarningEverywhereAsync(user, embed.Build());
                }
               
            });
        }

        private async Task RemoveWarningInternalAsync(IUser user, long id, string reason, bool force)
        {
            var warning = await Database.GetWarningAsync(id);
            if (warning == null)
                throw new Exception("Invalid warning ID.");
            if (warning.UserId != user.Id) // this check exists to catch typos in warning IDs
                throw new Exception("Warning ID does not match with user!");
            if (warning.IssuerId != Context.User.Id && !force)
                throw new Exception("Warning can only be deleted by its issuer, " + MentionUtils.MentionUser(warning.IssuerId) + ".");
            await Database.RemoveWarningAsync(id, this.Context.Message, reason);

            var state = WarningState.FromDatabase(await Database.GetWarningsAsync(user.Id));
            var embed = AddWarningsToEmbed(GetWarningEmbed(user, warning), state)
                .WithDescription("**A warning was deleted.**")
                .Build();

            await AnnounceWarningEverywhereAsync(user, embed);
        }

        private string GetUnmentionedUser(ulong userId)
        {
            var user = this.Context.Guild.GetUser(userId);
            return user.Username.DiscordEscape() + "#" + user.Discriminator;
        }

        private async Task ListWarningsAsync(string message, ICollection<Warning> warnings, IMessageChannel target)
        {
            var warningEmbeds = new List<EmbedBuilder> { new EmbedBuilder() };
            foreach (var w in warnings)
            {
                var warningStr =
                    $"**{(w.Amount == 0 ? "Initial Warning" : w.Amount == 1 ? "Warning" : "Strike")} {w.Id} " +
                    $"({w.IssueDate:MM/dd/yyyy}{(w.RemoveDate.HasValue ? $", removed on {w.RemoveDate.Value:MM/dd/yyyy}" : "")})**\n" +
                    $"{w.Reason}\n" +
                    (w.RemoveReason == null ? "" : $"Remove Reason: {w.RemoveReason}\n") +
                    $"[{this.GetUnmentionedUser(w.IssuerId)}](https://discordapp.com/channels/{DiscordSettings.GuildId}/{w.ChannelId}/{w.MessageId}) " +
                    $"in {MentionUtils.MentionChannel(w.ChannelId)} " +
                    (w.RemoveChannelId.HasValue && w.RemoveMessageId.HasValue && w.RemoverId.HasValue
                        ? $" | Removed by [{this.GetUnmentionedUser(w.RemoverId.Value)}](https://discordapp.com/channels/{DiscordSettings.GuildId}/{w.RemoveChannelId.Value}/{w.RemoveMessageId.Value}) in {MentionUtils.MentionChannel(w.ChannelId)}\n\n"
                        : "\n\n");

                if (warningEmbeds.Last().Description?.Length + warningStr.Length > 2048)
                {
                    warningEmbeds.Add(new EmbedBuilder());
                }

                warningEmbeds.Last().Description += warningStr;
            }

            await target.SendMessageAsync(message, embed: warnings.Count > 0 ? warningEmbeds[0].Build() : null);

            foreach (var embedBuilder in warningEmbeds.Skip(1))
            {
                await target.SendMessageAsync(embed: embedBuilder.Build());
            }
        }

        private Criteria<SocketMessage> GetCriteria(IMessageChannel channel)
        {
            var criteria = new Criteria<SocketMessage>();
            criteria.AddCriterion(new EnsureFromChannelCriterion(channel));
            criteria.AddCriterion(new EnsureFromUserCriterion(this.Context.User));
            return criteria;
        }


        [Command("list"), RequireChannel(DiscordSettings.BotCommandsChannel), Priority(1)]
        public async Task ListWarns(IUser user = null)
        {
            if (user == null) user = this.Context.User;
            var warnings = await Database.GetWarningsAsync(user.Id);
            await this.ListWarningsAsync($"**{this.GetUnmentionedUser(user.Id)}** has {warnings.Length} warnings.", warnings, this.Context.Channel);
        }

        [Command("status"), Alias("stats"), RequireChannel(DiscordSettings.BotCommandsChannel), Priority(1)]
        public async Task WarnStats(IUser user = null)
        {
            if (user == null) user = this.Context.User;
            var state = WarningState.FromDatabase(await Database.GetWarningsAsync(user.Id));
            var embed = AddWarningsToEmbed(new EmbedBuilder().WithAuthor(user), state).Build();
            await this.ReplyAsync(embed: embed);
        }

        [Command("force remove"), RequireRole(DiscordSettings.DiscordServerOwner), Priority(1)]
        public async Task ForceRemoveWarn(IUser user, long id, [Remainder] string reason)
        {
            await this.RemoveWarningInternalAsync(user, id, reason, true);
        }

        [Command("remove"), RequireRole(DiscordSettings.DiscordStaff), Priority(1)]
        public async Task RemoveWarn(IUser user, long id, [Remainder] string reason)
        {
            await this.RemoveWarningInternalAsync(user, id, reason, false);
        }

        [Command("force edit"), RequireRole(DiscordSettings.DiscordStaff), Priority(1)]
        public async Task ForceEditWarn(IUser user, long id, [Remainder] string reason = null)
        {
            await EditWarningInternalAsync(user, id, reason, true);
        }


        [Command("edit"), RequireRole(DiscordSettings.DiscordStaff), Priority(1)]
        public async Task EditWarn(IUser user, long id, [Remainder] string reason = null)
        {
            await EditWarningInternalAsync(user, id, reason, false);
        }

        [Command, RequireRole(DiscordSettings.DiscordStaff), Priority(0)]
        public async Task Warn(IGuildUser user, [Remainder] string reason)
        {
            if (user.RoleIds.Contains(DiscordSettings.DiscordStaff))
                throw new Exception("User is staff.");

            var history = await Database.GetWarningsAsync(user.Id);

            var warning = new Warning
            {
                UserId = user.Id,
                ChannelId = Context.Channel.Id,
                MessageId = Context.Message.Id,
                IssuerId = Context.User.Id,
                IssueDate = DateTime.UtcNow,
                Reason = reason
            };

            var tempEmbed = GetWarningEmbed(user, warning)
                .WithTitle("**A warning is being issued...**")
                .WithColor(Color.Blue)
                .WithFooter("")
                .Build();

            var msg = await this.ReplyAsync(embed: tempEmbed);
            try
            {
                var state = await ApplyWarningWithSeverity(user, history, warning, tempEmbed);
                await Database.AddWarningAsync(warning);

                var embed = GetWarningIssuedEmbed(user, warning, state);
                await AnnounceWarningEverywhereAsync(user, embed.Build());
            }
            catch (Exception ex)
            {
                await this.Context.User.SendMessageAsync(ex.Message);
            } 
            finally
            {
                await msg.DeleteAsync();
            }
        }

        private async Task AnnounceWarningEverywhereAsync(IUser user, Embed embed)
        {
            await this.ReplyAsync(embed: embed);
            await this.Context.User.SendMessageAsync(embed: embed);
            await user.SendMessageAsync(embed: embed);
            await this.Context.Client
                .GetGuild(DiscordSettings.GuildId)
                .GetTextChannel(DiscordSettings.LogsChannel)
                .SendMessageAsync(embed: embed);
        }

        private async Task<WarningState> ApplyWarningWithSeverity(IUser user, Warning[] history, Warning warning, Embed tempEmbed)
        {
            var state = WarningState.FromDatabase(history);

            warning.Amount = await RequestSeverity(user, history, this.AddWarningsToEmbed(tempEmbed.ToEmbedBuilder(), state).Build());
            if (history.Contains(warning))
                state = WarningState.FromDatabase(history);
            else
                state.Add(warning.Amount, warning.IssueDate);

            if (state.MutedUntil.HasValue)
            {
                await MuteWatcher.MuteUser(new Mute
                {
                    UserId = user.Id,
                    IssuerId = this.Context.Guild.CurrentUser.Id,
                    IssueDate = DateTime.UtcNow,
                    ExpiryDate = state.MutedUntil.Value.UtcDateTime
                }, "You got a strike!", true, true);
            } else
            {
                await MuteWatcher.UnmuteUser(user.Id, this.Context.Guild.CurrentUser.Id);
            }

            return state;
        }

        private async Task<int> RequestSeverity(IUser user, Warning[] history, Embed embed)
        {
            var initialWarnings = history.Where(w => w.Amount == 0 && !w.RemoveDate.HasValue).ToList();
            var dmChannel = await this.Context.User.GetOrCreateDMChannelAsync();
            await this.ListWarningsAsync($"{this.GetUnmentionedUser(user.Id)} has {initialWarnings.Count} initial warnings.", initialWarnings, dmChannel);
            await dmChannel.SendMessageAsync(embed: embed);
            await dmChannel.SendMessageAsync(
                "Select warning severity:\n" +
                "```swift\n" +
                "0 - Initial Warning (first violation of a specific rule)\n" +
                "1 - Warning\n" +
                "3 - Strike\n" +
                "```");
            var msg = await this.NextMessageAsync(GetCriteria(dmChannel), timeout: TimeSpan.FromMinutes(5));
            if (msg == null)
                throw new Exception("Timeout, operation cancelled.");
            if (!int.TryParse(msg.Content, out var amount))
                throw new Exception("Invalid input, operation cancelled.");
            if (amount != 0 && amount != 1 && amount != 3)
                throw new Exception("Invalid input, operation cancelled.");
            return amount;
        }
    }

    [Group("mod"), Summary("These are moderator-specific commands."), RequireRole(DiscordSettings.DiscordStaff)]
    public class ModeratorCommands : ModuleBase<SocketCommandContext>
    {
        [Group("18+"), Alias("18+", "18"), Summary("Give 18+ role to people")]
        public class EighteenPlusRole : ModuleBase<SocketCommandContext>
        {
            [Command("give")]
            public async Task GiveRole(IGuildUser user)
            {
                if (user.IsStaff())
                {
                    throw new Exception("You may not give the role to staff.");
                }

                if (!TryGetRole(user.Guild, out var muted))
                {
                    throw new Exception("Unable to get role from guild user.");
                }

                await user.AddRoleAsync(muted, new RequestOptions
                {
                    AuditLogReason = "Manual Mute Role Give by " + this.Context.User.Username.DiscordEscape(),
                }).ConfigureAwait(false);

                await this.Context.Message.DeleteAsync();
            }

            [Command("remove"), Alias("rm")]
            public async Task RemoveRole(IGuildUser user)
            {
                // intentionally allowing removing the muted role from staff

                if (!TryGetRole(user.Guild, out var muted))
                {
                    throw new Exception("Unable to get role from guild user.");
                }

                // TODO: moderators could accidentally cause some kind of discord formatting crud if their name is weird
                await user.RemoveRoleAsync(muted, new RequestOptions
                {
                    AuditLogReason = "Manual Role Removed by " + this.Context.User.Username,
                }).ConfigureAwait(false);

                await this.Context.Message.DeleteAsync();
            }

            private bool TryGetRole(IGuild guild, out IRole muted)
            {
                var query = guild.Roles
                    .Where(role => role.Id == DiscordSettings.EighteenRole)
                    .ToList();

                if (query.Count == 0)
                {
                    muted = default(IRole);
                    return false;
                }

                muted = query[0];
                return true;
            }
        }
    }
}