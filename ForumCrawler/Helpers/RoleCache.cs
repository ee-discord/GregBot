using Discord;
using Discord.WebSocket;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiscordSocialScore
{
    public class RoleCacheProvider
    {

        public DiscordSocketClient Client { get; }
        private readonly Dictionary<ulong, RoleCache> cacheCollection = new Dictionary<ulong, RoleCache>();

        public RoleCacheProvider(DiscordSocketClient client)
        {
            this.Client = client;
        }

        public RoleCache Get(SocketGuild guild)
        {
            if (!cacheCollection.TryGetValue(guild.Id, out var result))
            {
                result = cacheCollection[guild.Id] = new RoleCache(this.Client, guild);
            }
            return result;
        } 
    }

    public class RoleCache
    {
        private readonly AsyncLock _mutex = new AsyncLock();
        public SocketGuild Guild { get; }
        private readonly Dictionary<string, IRole> cache = new Dictionary<string, IRole>();

        public RoleCache(DiscordSocketClient client, SocketGuild guild)
        {
            this.Guild = guild;
            client.RoleCreated += this.Client_RoleCreated;
        }

        private Task Client_RoleCreated(SocketRole arg)
        {
            if (arg.Guild == this.Guild)
            {
                var toDelete = this.cache.Where(kv => this.Guild.Roles.Any(r => r.Id == kv.Value.Id)).Select(kv => kv.Key).ToList();
                foreach (var key in toDelete)
                {
                    this.cache.Remove(key);
                }
            }
            return Task.CompletedTask;
        }

        public async Task<IRole> CreateOrUpdateRoleAsync(string roleName, GuildPermissions? permissions = null, Color color = default(Color), bool isHoisted = false)
        {
            var name = ScoreRoleManager.RolePrefix + roleName;
            IRole res = Guild.Roles.FirstOrDefault(r => r.Name == name);

            if (res == null)
            {
                using (await _mutex.LockAsync())
                {
                    this.cache.TryGetValue(name, out res);

                    if (res == null)
                    {
                        res = this.cache[name] = await Guild.CreateRoleAsync(name, permissions, color, isHoisted, null);
                    }
                }
            }

            if (res.Color != color || res.IsHoisted != isHoisted)
            {
                await res.ModifyAsync(p =>
                {
                    p.Color = color;
                    p.Hoist = isHoisted;
                });
            }

            return res;
        }
    }
}