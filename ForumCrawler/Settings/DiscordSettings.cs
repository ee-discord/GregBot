using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Discord;
using Discord.Addons.Interactive;
using Discord.Commands;
using Discord.WebSocket;

using ForumCrawler.Commands;

using Microsoft.Extensions.DependencyInjection;

namespace ForumCrawler
{
    public static class DiscordSettings
    {
#if DEBUG
        public const string CommandPrefix = "dbg!";
#else
        public const string CommandPrefix = "g!";
#endif
        public const string UrlPrefix = "https://forums.everybodyedits.com/viewtopic.php?pid=";

        public const ulong GuildId = 329335303963803649;
        public const ulong BotCommandsChannel = 329634826061742081;
        public const ulong StaffCommandsChannel = 569634941252534302;
        public const ulong LogsChannel = 330328534780346378;
        public const ulong ForumChannel = 329855149549813760;
        public const ulong DiscordStaffConsultant = 529386148682924036;
        public const ulong DiscordStaff = 329400136021049348;
        public const ulong DiscordServerOwner = 395419811372597249;
        public const ulong MutedRole = 333618650634387458;
        public const ulong EighteenRole = 407999201830764544;
        public const ulong StarboardChannel = 589926820825006120;
        public const ulong UnverifiedChannel = 707659903355060226;
        public const ulong BannedEmote = 614156104682962954;

        public const ulong DSGuildId = 705927053874167909;
        public const ulong DSVoteboardChannel = 705937336055365734;
        public const ulong DSModerationChannel = 705974945817034773;
        public const ulong DSDiscordStaffConsultant = 705928172641714227;
        public const ulong DSDiscordStaff = 705932947294781510;
        public const ulong DSDiscordServerOwner = 705928497851531325;
        public const ulong DSStaffCommandsChannel = 705936434489589822;
        public const ulong DSReportsChannel = 705933908008763503;
        public const ulong SuggestionBoard = 731977062931234887;
        public const ulong Feedback = 549402587116470283;
        public const ulong GovernanceSubscriptionFeed = 765456947322552320;
        public const ulong VoiceRole = 779048916687519755;

        public static Dictionary<SubscriptionType, ulong> MapSubscriptionToRole = new Dictionary<SubscriptionType, ulong>
        {
            [SubscriptionType.Events] = 749734307379150898,
            [SubscriptionType.Governance] = 765456742812614656,
        };

        public static Dictionary<ulong, GovernanceConfig> GovernanceConfigs = new Dictionary<ulong, GovernanceConfig>
        {
            {
                GuildId,
                new GovernanceConfig(549399741994237984, 549402714103087144,
                    new OverwritePermissions(viewChannel: PermValue.Allow))
            },
            {
                DSGuildId,
                new GovernanceConfig(705937563021737985, 705940856099569714,
                    new OverwritePermissions(sendMessages: PermValue.Allow))
            }
        };

        public class GovernanceConfig
        {
            public GovernanceConfig(ulong category, ulong changelogChannel,
                OverwritePermissions everyonePermissionsAfterSubmission)
            {
                Category = category;
                ChangelogChannel = changelogChannel;
                EveryonePermissionsAfterSubmission = everyonePermissionsAfterSubmission;
            }

            public ulong Category { get; }
            public ulong ChangelogChannel { get; }
            public OverwritePermissions EveryonePermissionsAfterSubmission { get; }
        }

        public static async Task<DiscordSocketClient> GetClient()
        {
            var client = new DiscordSocketClient(new DiscordSocketConfig
            {
                MessageCacheSize = 50, AlwaysDownloadUsers = true
            });
            client.Log += Log;
            client.AddOnFirstReady(() => Ready(client));

            await client.LoginAsync(TokenType.Bot, Passwords.DiscordToken);
            await client.StartAsync();
            await client.SetStatusAsync(UserStatus.Online);

            return client;
        }

        private static async Task Ready(DiscordSocketClient client)
        {
            await client.SetGameAsync("Greg Simulator " + DateTimeOffset.UtcNow.Year);
        }

        private static Task Log(LogMessage message)
        {
            Console.WriteLine(message.ToString());
            return Task.CompletedTask;
        }

        public static async Task<CommandService> InstallCommands(DiscordSocketClient client, IServiceProvider services)
        {
            // Hook the MessageReceived Event into our Command Handler
            var commands = new CommandService();
            commands.Log += Log;
            client.MessageReceived += msg => HandleCommand(client, services, commands, msg);
            // Discover all of the commands in this assembly and load them.
            commands.AddTypeReader(typeof(IUser), new MainGuildUserTypeReader<IUser>());
            commands.AddTypeReader(typeof(IGuildUser), new MainGuildUserTypeReader<IGuildUser>());
            commands.AddTypeReader<IGupUser>(new MainGuildUserTypeReader<IGupUser>(GupUser.BehaviorOverrides), true);
            await commands.AddModulesAsync(Assembly.GetEntryAssembly(), services);
            return commands;
        }

        public static IServiceProvider ConfigureServices(DiscordSocketClient client)
        {
            var services = new ServiceCollection()
                .AddSingleton(client)
                .AddSingleton<InteractiveService>();
            var provider = services.BuildServiceProvider();
            return provider;
        }

        public static Task HandleCommand(DiscordSocketClient client, IServiceProvider services, CommandService commands,
            SocketMessage messageParam)
        {
            if (!(messageParam is SocketUserMessage message))
            {
                return Task.Delay(0);
            }

            Task.Run(async () =>
            {
                var argPos = 0;
                if (!(message.HasStringPrefix(CommandPrefix, ref argPos) ||
                      message.HasMentionPrefix(client.CurrentUser, ref argPos)))
                {
                    return;
                }

                {
                    var context = new SocketCommandContext(client, message);
                    var result = await commands.ExecuteAsync(context, argPos, services);
                    if (!result.IsSuccess)
                    {
                        await context.Message.Channel.SendErrorAsync(result.ErrorReason);
                    }
                }
            });

            return Task.Delay(0);
        }
    }
}
