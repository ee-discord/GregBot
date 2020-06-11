using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Rest;
using Discord.WebSocket;

namespace ForumCrawler
{
    public static class StarboardWatcher
	{
		public static void Bind(DiscordSocketClient client)
        {
            client.ReactionAdded += Client_ReactionAdded;
            client.ReactionRemoved += Client_ReactionRemoved;
        }

        private static bool IsWootReaction(IEmote emote) => emote.Name == "woot";

        private static IEnumerable<SocketCategoryChannel> GetAllowedCategoryChannels(SocketGuild guild)
        {
            yield return guild.GetCategoryChannel(360825166437285891); // text channels
        }

        private static bool ChannelAllowed(SocketGuildChannel socketGuildChannel)
        {
            foreach (var allowed in GetAllowedCategoryChannels(socketGuildChannel.Guild))
            {
                if (allowed.Channels.Any(channel => channel.Id == socketGuildChannel.Id))
                {
                    return true;
                }
            }

            return false;
        }

		private static async Task HandleReaction(SocketReaction reaction)
		{
			if (!IsWootReaction(reaction.Emote)
				|| !(reaction.Channel is SocketGuildChannel socketGuildChannel)
				|| !ChannelAllowed(socketGuildChannel))
			{
				return;
			}

			var reactionMessage = await reaction.Channel.GetMessageAsync(reaction.MessageId);

			if (!(reactionMessage is IUserMessage userMessage))
			{
				Console.WriteLine("Handle reaction - reaction message not user message");
				return;
			}

			if (!(userMessage.Author is IGuildUser user))
			{
				Console.WriteLine("Handle reaction - user author not guild user");
				return;
			}

			var gazers = (await userMessage.GetReactionUsersAsync(reaction.Emote, 1000).ToArrayAsync().ConfigureAwait(false))
				.SelectMany(page => page)
				.Where(u => u != null)
				.ToList();

			var shouldBeOnStarboard = gazers.Count >= DiscordSettings.MinStarboardReactions;
			var messageId = reaction.MessageId;

			var starboardInformation = await Database.GetStarboardInformation(reaction.MessageId).ConfigureAwait(false);

			var currentlyOnStarboard = starboardInformation.OnStarboard;
			var starboardMessageId = starboardInformation.StarboardMessageId;

			// don't do anything in these circumstances
			if ((currentlyOnStarboard && shouldBeOnStarboard)
				|| (!currentlyOnStarboard && !shouldBeOnStarboard))
			{
                // if we shouldn't be on starboard, don't update anything
				if (!shouldBeOnStarboard)
				{
					return;
				}

				var starboardMessage = await GetStarboardMessage(starboardMessageId).ConfigureAwait(false);

				// just update the message to reflect the current value
				var message = await reaction.Channel.GetMessageAsync(reaction.MessageId).ConfigureAwait(false);

				var embed = DiscordFormatting.BuildStarboardEmbed(user, message, gazers.Count)
					.Build();

				await starboardMessage.ModifyAsync(properties => properties.Embed = embed).ConfigureAwait(false);
			}
            else if (currentlyOnStarboard && !shouldBeOnStarboard)
			{
				// get off the board

				var starboardMessage = await GetStarboardMessage(starboardMessageId).ConfigureAwait(false);
				await starboardMessage.DeleteAsync().ConfigureAwait(false);
				await Database.DeleteStarboardEntry(messageId);
			}
			else if (!currentlyOnStarboard && shouldBeOnStarboard)
			{
				// get on the board

				var message = await reaction.Channel.GetMessageAsync(reaction.MessageId).ConfigureAwait(false);
				var embed = DiscordFormatting.BuildStarboardEmbed(user, message, gazers.Count)
					.Build();

				var starboardMessage = await GetStarboardChannel()
					.SendMessageAsync(embed: embed)
					.ConfigureAwait(false);

				await Database.CreateStarboardEntry(messageId, starboardMessage.Id);
			}

			return;

			async Task<IUserMessage> GetStarboardMessage(ulong starboardMessageId2)
            {
				return (IUserMessage)(await GetStarboardChannel()
					.GetMessageAsync(starboardMessageId2).ConfigureAwait(false));
			}

            SocketTextChannel GetStarboardChannel()
            {
				return socketGuildChannel.Guild
					.GetTextChannel(DiscordSettings.StarboardChannel);
			}
		}

		private static Task Client_ReactionRemoved
		(
			Cacheable<IUserMessage, ulong> cachableMessage,
			ISocketMessageChannel socketChannel,
			SocketReaction reaction
		)
			=> HandleReaction(reaction);

        private static Task Client_ReactionAdded
        (
            Cacheable<IUserMessage, ulong> message,
            ISocketMessageChannel channel,
            SocketReaction reaction
		)
			=> HandleReaction(reaction);
	}
}
