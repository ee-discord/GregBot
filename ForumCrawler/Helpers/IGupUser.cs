using Discord;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ForumCrawler
{
    // The "IGupUser" exists to serve as a placeholder wherever gup aliases are applicable.
    // See Task<CommandService> DiscordSettings.InstallCommands(DiscordSocketClient, IServiceProvider) for its usage.
    // TODO: throw out once stuff gets fixed

    public interface IGupUser
	{
        IUser ActualUser { get; }
	}

	public class GupUser : IGupUser
    {
        public static GupAliasBehaviorOverrides<IGupUser> BehaviorOverrides => new GupAliasBehaviorOverrides<IGupUser>
        {
            GupAliases = new Dictionary<string, ulong>
            {
                ["atilla"] = 676936491615780867,
                ["bunny"] = 676936491615780867,
                ["miou"] = 676936491615780867,

                ["mini"] = 431265335287611393,
                ["minimania"] = 431265335287611393,
                ["crybaby"] = 431265335287611393,

                ["tora"] = 373864804349378561,

                ["tund"] = 284378073774817281,

                ["karl"] = 268856125007331328,
                ["karl_255"] = 268856125007331328,

                ["kirbyk"] = 498183829388001292,
                ["kirbykareem"] = 498183829388001292,
                ["kareem"] = 498183829388001292,

                ["eleizibeth"] = 308753702762708993,
                ["elei"] = 308753702762708993,

                ["sirjosh"] = 172465767000965120,
                ["sir"] = 172465767000965120,
                ["josh"] = 172465767000965120,

                ["task"] = 192620017882234880,
                ["taskmanager"] = 192620017882234880,
                ["averagealien"] = 192620017882234880,

                ["macac"] = 327296893665280000,
                ["mac"] = 327296893665280000,

                ["kkay"] = 339869489724391435,
                ["kay"] = 339869489724391435,

                ["toma"] = 431529354355408897,

                ["mike"] = 265467042294005760,

                ["miner"] = 427941289053388810,

                ["sarah"] = 691351852855328810,
                
                ["orton"] = 209777989280333834,
                ["32"] = 209777989280333834,

                ["john"] = 188359912869462016,
                ["emalton"] = 188359912869462016,
                ["notlame"] = 188359912869462016,

                ["zoey"] = 145949569497300992,

                ["phinarose"] = 281188209956880394,
                ["phina"] = 281188209956880394,
                ["rose"] = 281188209956880394,

                ["gosha"] = 252733339734245376,
                ["goshanoob"] = 252733339734245376,

            },

            UserToT = (user) => new GupUser(user),
            UserIdToT = async (userId, guild) => new GupUser(
                await guild.GetUserAsync(userId, CacheMode.CacheOnly).ConfigureAwait(false)
            ),
        };

        public GupUser(IUser user)
		{
            ActualUser = user;
		}

		public IUser ActualUser { get; set; }
	}
}
