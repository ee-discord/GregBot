using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

namespace ForumCrawler
{
    public static class DiscordFormatting
    {
        public static EmbedBuilder BuildStarboardEmbed(IGuildUser author, IMessage message, int gazers, bool makeRed)
        {
            var builder = new EmbedBuilder()
                .WithAuthor(author.GetName(), author.GetAvatarUrl(size: 20))
                .WithDescription(message.Content)
                .WithFooter("In #" + message.Channel.Name)
                .WithTimestamp(message.CreatedAt)
                .WithFields
                (
                    new EmbedFieldBuilder()
                        .WithIsInline(true)
                        .WithName("Original:")
                        .WithValue($"[Click here]({message.GetJumpUrl()})"),
                    new EmbedFieldBuilder()
                        .WithIsInline(true)
                        .WithName("Score:")
                        .WithValue(gazers + " :fire:")
                ); // :)

            if (makeRed)
            {
                builder.WithColor(12345678u);
            }
            else
            {
                builder.WithColor(Color.DarkGrey);
            }

            foreach (var attach in message.Attachments)
            {
                builder.WithImageUrl(attach.Url);
            }

            return builder;
        }

        public static (List<string> content, string media) BBCodeToMarkdown(string bbCode)
        {
            var media = new List<string>();
            var tagStack = new Stack<string>();
            var extraStack = new Stack<string>();
            var res = new List<string>();
            var currStr = new StringBuilder();

            void commit()
            {
                res.Add(currStr.ToString());
                currStr = new StringBuilder();
            }

            var matchesUnsafe = Regex.Matches(bbCode, @"\[code\](.*)\[\/code\]|\[(\/?[a-z]+)(=[^\]]+)?\]|([^\[]+)",
                RegexOptions.Multiline & RegexOptions.IgnoreCase);
            foreach (var matchUnsafe in matchesUnsafe.Cast<Match>().Skip(1).Take(matchesUnsafe.Count - 3))
            {
                if (matchUnsafe.Groups[1].Success) // code tag
                {
                    if (!tagStack.Contains("quote"))
                    {
                        currStr.Append("```").Append(matchUnsafe.Groups[1].Value.Replace("```", "` ` `")).Append("```");
                    }
                }
                else if (matchUnsafe.Groups[2].Success) // other tag
                {
                    var tagAndModifierUnsafe = matchUnsafe.Groups[2].Value;
                    var tag = tagAndModifierUnsafe.TrimStart('/').DiscordEscape().ToLower();
                    var extra = matchUnsafe.Groups[3].Value.DiscordEscape();
                    extra = extra.Substring(Math.Min(extra.Length, 1));
                    var open = !tagAndModifierUnsafe.StartsWith("/");

                    string lastTag = null;
                    string lastExtra = null;
                    if (!open)
                    {
                        lastTag = tagStack.Pop();
                        lastExtra = extraStack.Pop();
                    }

                    switch (tag)
                    {
                        case "b":
                            currStr.Append("**");
                            break;

                        case "i":
                            currStr.Append("*");
                            break;

                        case "u":
                            currStr.Append("__");
                            break;

                        case "s":
                            currStr.Append("~~");
                            break;

                        case "h":
                            commit();
                            break;

                        case "cw":
                            goto case "spoiler";
                        case "spoiler":
                            currStr.AppendLine().AppendLine();
                            goto case "hide";
                        case "hide":
                            if (!tagStack.Contains("cw") && !tagStack.Contains("hide") && !tagStack.Contains("spoiler"))
                            {
                                currStr.Append("||");
                            }

                            break;

                        case "pre":
                            currStr.AppendLine().AppendLine();
                            break;

                        case "quote":
                            if (!tagStack.Contains("quote") && !tagStack.Contains("pre") && open)
                            {
                                currStr.AppendLine().AppendLine();
                                if (extra.Length > 0)
                                {
                                    currStr.Append("(quoting ").Append(extra).Append(')');
                                }
                                else
                                {
                                    currStr.Append("(quote ommited)");
                                }

                                currStr.AppendLine().AppendLine();
                            }

                            break;

                        case "youtube":
                            if (open && extra != "")
                            {
                                currStr.Append('[').Append(extra).Append("](");
                            }

                            if (!open && lastExtra != "")
                            {
                                currStr.Append(") ");
                            }

                            if (open)
                            {
                                currStr.Append("https://www.youtube.com/watch?v=");
                            }

                            break;

                        case "world":
                            if (open)
                            {
                                currStr.Append("https://everybodyedits.com/games/");
                            }

                            break;

                        case "img":
                            if (open && extra != "")
                            {
                                currStr.Append('[').Append(extra).Append("](");
                            }

                            if (!open && lastExtra != "")
                            {
                                currStr.Append(") ");
                            }

                            break;

                        case "url":
                            if (open && extra != "")
                            {
                                currStr.Append(" [");
                            }

                            if (!open && lastExtra != "")
                            {
                                currStr.Append("](").Append(lastExtra).Append(") ");
                            }

                            break;

                        case "list":
                            currStr.AppendLine();
                            if (!open)
                            {
                                currStr.AppendLine();
                            }

                            break;

                        case "item":
                            if (open)
                            {
                                currStr.AppendLine().Append("- ");
                            }

                            break;

                        case "color":
                            break;

                        default:
                            currStr.Append('[').Append(tagAndModifierUnsafe.DiscordEscape()).Append(']');
                            break;
                    }

                    if (open)
                    {
                        tagStack.Push(tag);
                        extraStack.Push(extra);
                    }
                }
                else if (matchUnsafe.Groups[4].Success)
                {
                    if (tagStack.Contains("quote"))
                    {
                        continue;
                    }

                    var value = matchUnsafe.Groups[4].Value.Trim('\n').DiscordEscape();

                    if (tagStack.Any())
                    {
                        switch (tagStack.Peek())
                        {
                            case "img":
                                media.Add(value);
                                break;

                            case "world":
                                media.Add($"https://mm.sirjosh3917.com/{value}?scale=1");
                                break;

                            case "youtube":
                                media.Add("https://www.youtube.com/watch?v=" + value);
                                break;
                        }
                    }

                    currStr.Append(value);
                }
            }

            commit();
            return (res, string.Join(" ", media));
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

        public static async Task AnnouncePostAsync(DiscordSocketClient client, Post post)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            var (content, media) = BBCodeToMarkdown(post.Content);

            var embed = new EmbedBuilder()
                .WithAuthor(post.Poster,
                    string.Format("https://forums.everybodyedits.com/img/avatar.php?id={0}", post.PosterId),
                    "https://forums.everybodyedits.com/profile.php?id=" + post.PosterId)
                .WithFooter($"In {GetForumShortName(post.Forum).DiscordEscape()}")
                .WithTimestamp(DateTimeOffset.Parse(post.Time))
                .WithUrl($"{DiscordSettings.UrlPrefix + post.PostId}#p{post.PostId}")
                .WithTitle($"#{post.Pnumber} in \"{post.Topic.DiscordEscape()}\"")
                .WithDescription(content[0].Substring(0, Math.Min(content[0].Length, 2048)));

            for (var i = 1; i <= content.Count / 2; i++)
            {
                var title = content[(i * 2) - 1];
                var value = content[i * 2];

                embed.AddField(title.Substring(0, Math.Min(title.Length, 256)),
                    value.Substring(0, Math.Min(value.Length, 1024)));
            }

            var mediaMsg = media.Length > 0 ? "Referenced media: " + media : null;

#if DEBUG
            Console.WriteLine("Announcing post");
#else
            await client
                .GetGuild(DiscordSettings.GuildId)
                .GetTextChannel(DiscordSettings.ForumChannel)
                .SendMessageAsync(mediaMsg, embed: embed.Build());
#endif
        }

        public static async Task AnnounceDownAsync(DiscordSocketClient discord)
        {
            await discord
                .GetGuild(DiscordSettings.GuildId)
                .GetTextChannel(DiscordSettings.ForumChannel)
                .SendErrorAsync("[__**Error**__] Forums appear to be down.");
        }

        private static string GetForumShortName(string longName)
        {
            switch (longName)
            {
                case "Bots and Programming":
                    return "B&P";

                case "Questions and Answers":
                    return "Q&A";

                case "Bug Reports":
                    return "Bugs";

                case "Off-Topic Discussion":
                    return "Off-Topic";

                default:
                    return longName;
            }
        }

        public static StringBuilder TrimEnd(this StringBuilder sb)
        {
            if (sb == null || sb.Length == 0)
            {
                return sb;
            }

            var i = sb.Length - 1;
            for (; i >= 0; i--)
            {
                if (!char.IsWhiteSpace(sb[i]))
                {
                    break;
                }
            }

            if (i < sb.Length - 1)
            {
                sb.Length = i + 1;
            }

            return sb;
        }
    }
}
