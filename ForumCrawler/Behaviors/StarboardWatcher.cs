using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

namespace ForumCrawler
{
    public class StarboardWatcher
    {
        private readonly EmoteQualifier _adminQualifier;
        private readonly ChannelQualifier _channelQualifier;

        private readonly DiscordSocketClient _client;
        private readonly int _configuredWoots;
        private readonly EmoteQualifier _emoteQualifier;
        private readonly SocketGuild _guild;
        private readonly SocketTextChannel _starboard;
		private readonly EmoteQualifier _emoteDisqualifier;

		public StarboardWatcher
        (
            DiscordSocketClient client,
            SocketGuild guild,
            SocketTextChannel starboard,
            ChannelQualifier channelQualifier,
            EmoteQualifier emoteQualifier,
            int configuredWoots,
            EmoteQualifier emoteDisqualifier = null
        )
        {
            _client = client;
            _guild = guild;
            _starboard = starboard;
            _channelQualifier = channelQualifier;
            _emoteQualifier = emoteQualifier;
            _adminQualifier = IsAdminNoVisibilityEmote;
            _configuredWoots = configuredWoots;
            _emoteDisqualifier = emoteDisqualifier;
            _client.ReactionAdded += (a, b, c) => OnReactionChanged(a, b, c, true);
            _client.ReactionRemoved += (a, b, c) => OnReactionChanged(a, b, c, false);
		}

        private static bool IsAdminNoVisibilityEmote(IEmote emote)
        {
            return emote.Name == "🚫"; // no_entry_sign
        }

        /// <summary>
        ///     Attaches event handlers to the client.
        /// </summary>
        public static void Bind(DiscordSocketClient client)
        {
            client.AddOnFirstReady(() =>
            {
                StarboardWatcherConfigurator.GeneralStarboard(client);
                StarboardWatcherConfigurator.StaffVoteStarboard(client);
                StarboardWatcherConfigurator.SuggestionStarboard(client);
                return Task.CompletedTask;
            });
        }

        private Task OnReactionChanged
        (
            Cacheable<IUserMessage, ulong> message,
            ISocketMessageChannel channel,
            SocketReaction reaction,
            bool added
        )
        {
            var isStarboardableChannel = _channelQualifier(channel);

            if (!isStarboardableChannel)
            {
                return Task.CompletedTask;
            }

            var isStarboardEmote = _emoteQualifier(reaction.Emote);

            if (!isStarboardEmote && (_emoteDisqualifier != null ? (!_emoteDisqualifier(reaction.Emote)) : true))
            {
                var isAdminEmote = _adminQualifier(reaction.Emote);

                // admin check:
                // bug found by: tora <@373864804349378561> <https://discordapp.com/channels/329335303963803649/329335303963803649/717104011622219877>
                var isAdmin = false;

                if (reaction.User.IsSpecified
                    && reaction.User.Value is IGuildUser guildUser)
                {
                    isAdmin = guildUser.IsStaff();
				}

                if (isAdminEmote && isAdmin)
                {
                    return OnStaffVisibility(message, added);
                }

                return Task.CompletedTask;
            }

            return OnReactionChangedImpl(message);
        }

        private async Task OnReactionChangedImpl(Cacheable<IUserMessage, ulong> message)
        {
            await OnUpdateAllReactions(await message.GetOrDownloadAsync());
        }

        private async Task OnUpdateAllReactions
        (
            IUserMessage message
        )
        {
            var reactors = await GetReactionUsers(message);

            List<StarReaction> gazers;

            // 0 reactors - kill anyone who is currently reacted to it
            using (var ctx = new DatabaseContext())
            {
                gazers = await ctx.StarboardGazers
                    .Where(gazer => gazer.StarboardChannelId == (long)_starboard.Id
                                    && gazer.MessageId == (long)message.Id)
                    .ToListAsync();

                // for every user that has reacted that *isn't* a gazer, we will add them
                foreach (var user in reactors)
                {
                    // if there's not a gazer in the DB (and they aren't the original poster) with the user reacting
                    if (!gazers.Any(gazer => gazer.StargazerId == (long)user.Id) && (ulong)gazer.StargazerId != message.Author.Id)
                    {
                        // add them
                        await OnReactionAddedImpl(ctx, message, user.Id, true);
                    }
                }

                // for every user that *hasn't* reacted that *is* a gazer, we will remove them
                foreach (var gazer in gazers)
                {
                    // if there's a reactor not in the DB with a gazer entry
                    if (!reactors.Any(user => user.Id == (ulong)gazer.StargazerId))
                    {
                        // remove them
                        await OnReactionAddedImpl(ctx, message, (ulong)gazer.StargazerId, false);
                    }
                }

                await ctx.SaveChangesAsync();
            }

            await UpdateStarboardPost(message);
        }

        private async Task OnStaffVisibility
        (
            Cacheable<IUserMessage, ulong> message,
            bool added
        )
        {
            using (var ctx = new DatabaseContext())
            {
                var post = await ctx.RevisedStarboardPosts.SingleOrDefaultAsync(loadingPost =>
                    loadingPost.MessageId == (long)message.Id
                    && loadingPost.StarboardChannelId == (long)_starboard.Id);

                if (post == default)
                {
                    // a post doesn't exist to censor - let's ignore this emote then

                    // ctx.RevisedStarboardPosts.Add(new RevisedStarboardPost
                    // {
                    //     MessageId = (long)message.Id,
                    //     StaffToggledVisibility = added,
                    //     StarboardChannelId = (long)_starboard.Id,
                    //     StarboardMessageId = 0
                    // });
                }
                else
                {
                    post.StaffToggledVisibility = added;
                }

                await ctx.SaveChangesAsync();
            }

            await UpdateStarboardPost(await message.GetOrDownloadAsync());
        }

        private async Task OnReactionAddedImpl
        (
            DatabaseContext ctx,
            IUserMessage reactionMessage,
            ulong reactorUserId,
            bool added
        )
        {
            // we don't save changes async - we expect the caller to do that

            if (!(reactionMessage.Author is IGuildUser user))
            {
                Console.WriteLine("Handle reaction - user author not guild user");
                return;
            }

            // we only want to add a reaction to the DB if one doesn't exist

            var reactionPost = await ctx.StarboardGazers.SingleOrDefaultAsync(gazer =>
                gazer.MessageId == (long)reactionMessage.Id
                && gazer.StarboardChannelId == (long)_starboard.Id
                && gazer.StargazerId == (long)reactorUserId);

            var reactionExists = reactionPost != default;

            if (!reactionExists && added)
            {
                ctx.StarboardGazers.Add(new StarReaction
                {
                    MessageId = (long)reactionMessage.Id,
                    StarboardChannelId = (long)_starboard.Id,
                    StargazerId = (long)reactorUserId
                });
            }
            else if (reactionExists && !added)
            {
                ctx.StarboardGazers.Remove(reactionPost);
            }
        }

        /// <summary>
        ///     Computes the logic necessary for if a starboard post should be shown or hidden.
        /// </summary>
        /// <param name="message">The original message posted.</param>
        private async Task UpdateStarboardPost(IUserMessage message)
        {
            using (var ctx = new DatabaseContext())
            {
                var post = await ctx.RevisedStarboardPosts.SingleOrDefaultAsync(loadingPost =>
                    loadingPost.MessageId == (long)message.Id
                    && loadingPost.StarboardChannelId == (long)_starboard.Id);
                var reactions = await ctx.StarboardGazers.CountAsync(reaction => reaction.MessageId == (long)message.Id
                                                                                 && reaction.StarboardChannelId ==
                                                                                 (long)_starboard.Id);

                var postExists = post != default;

                REDO_ALL_OF_THIS_LOGIC:
                if (postExists)
                {
                    // if this is true, we do not want the post to show up on starboard
                    if (post.StaffToggledVisibility)
                    {
                        if (post.StarboardMessageId != 0)
                        {
                            await _starboard.DeleteMessageAsync((ulong)post.StarboardMessageId);
                            post.StarboardMessageId = 0;
                        }
                    }
                    else
                    {
                        // we want to update/create the post
                        if (post.StarboardMessageId == 0)
                        {
                            // only want to re-create posts if they pass the threshold
                            if (reactions >= _configuredWoots)
                            {
                                var starboardMessage =
                                    await _starboard.SendMessageAsync(embed: GetMessageEmbed(message, reactions));
                                post.StarboardMessageId = (long)starboardMessage.Id;
                            }
                        }
                        else
                        {
                            var starboardMessage =
                                (IUserMessage)await _starboard.GetMessageAsync((ulong)post.StarboardMessageId);

                            // if we couldn't get the message, we need to create it
                            if (starboardMessage == null)
                            {
                                post.StarboardMessageId = 0;
                                postExists = false;
                                goto REDO_ALL_OF_THIS_LOGIC;
                            }

                            await starboardMessage.ModifyAsync(edit =>
                            {
                                edit.Embed = GetMessageEmbed(message, reactions);
                            });
                        }
                    }
                }
                else
                {
                    // post doesn't exist
                    // only want to re-create posts if they pass the threshold
                    if (reactions >= _configuredWoots)
                    {
                        var starboardMessage =
                        await _starboard.SendMessageAsync(embed: GetMessageEmbed(message, reactions));

                        if (post == default)
                        {
                            ctx.RevisedStarboardPosts.Add(new RevisedStarboardPost
                            {
                                MessageId = (long)message.Id,
                                StaffToggledVisibility = false,
                                StarboardChannelId = (long)_starboard.Id,
                                StarboardMessageId = (long)starboardMessage.Id
                            });
                        }
                        else
                        {
                            post.StarboardMessageId = (long)starboardMessage.Id;
                        }
                    }
                }

                await ctx.SaveChangesAsync();
            }
        }

        private Embed GetMessageEmbed(IUserMessage message, int woots)
        {
            return DiscordFormatting
                .BuildStarboardEmbed((IGuildUser)message.Author, message, woots, woots >= _configuredWoots).Build();
        }

        /// <summary>
        ///     Gets every unique user who has reacted to the message, based upon every
        ///     reaction added that qualifies.
        /// </summary>
        private async Task<IEnumerable<IUser>> GetReactionUsers(IUserMessage message)
        {
            IEnumerable<IUser> allUsers = Array.Empty<IUser>();

            foreach (var reaction in message.Reactions.Keys)
            {
                if (!_emoteQualifier(reaction))
                {
                    continue;
                }

                allUsers = allUsers
                    .Concat(await message.GetReactionUsersAsync(reaction, 1000).FlattenAsync());
            }

            // order the users so that when we skip over users things are fine
            var reactors = allUsers.Distinct(new UserIdEqualityComparer()).OrderBy(user => user.Id).ToArray();

            // bodged omega hackjob solution to support emote disqualifiers
            if (_emoteDisqualifier != null)
            {
                IEnumerable<IUser> allDisqualifiers = Array.Empty<IUser>();

                foreach (var reaction in message.Reactions.Keys)
                {
                    if (!_emoteDisqualifier(reaction))
                    {
                        continue;
                    }

                    allDisqualifiers = allDisqualifiers
                        .Concat(await message.GetReactionUsersAsync(reaction, 1000).FlattenAsync());
                }

                var disqualifiers = allDisqualifiers.Distinct(new UserIdEqualityComparer())
                    .OrderBy(user => user.Id).ToArray();

                // probably a very bad and not great but oh well :D
                return reactors.Skip(disqualifiers.Length);
            }
            else
            {
                return reactors;
			}
        }

        private class UserIdEqualityComparer : IEqualityComparer<IUser>
        {
            public bool Equals(IUser x, IUser y)
            {
                return x?.Id == y?.Id;
            }

            public int GetHashCode(IUser obj)
            {
                return obj == null ? default : obj.Id.GetHashCode();
            }
        }
    }
}
