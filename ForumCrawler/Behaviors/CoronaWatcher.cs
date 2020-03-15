﻿using Discord;
using Discord.WebSocket;
using HtmlAgilityPack;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ForumCrawler
{
    public interface ICoronaEntry
    {
        int Cases { get; }
        int Deaths { get; }
        int Recovered { get; }
        int Serious { get; }
        int Active { get; }
    }

    internal class CoronaData : ICoronaEntry
    {
        public List<int> CaseHistory { get; set; }
        public Dictionary<string, CoronaEntry> Entries { get; set; } = new Dictionary<string, CoronaEntry>();
        public DateTime LastUpdated { get; set; }

        public int Cases => this.Entries.Values.Sum(e => e.Cases);
        public int Active => this.Entries.Values.Sum(e => e.Active);
        public int Deaths => this.Entries.Values.Sum(e => e.Deaths);
        public int Recovered => this.Entries.Values.Sum(e => e.Recovered);
        public int Serious => this.Entries.Values.Sum(e => e.Serious);
        public int Mild => this.Active - this.Serious;

        public int RegionsActive => this.Entries.Values.Count(e => e.Active > 0);
        public int RegionsRecovered => this.Entries.Values.Count(e => e.Active == 0);

        public double DeathRate => (double)this.Deaths / (this.Recovered + this.Deaths);

        public enum CoronaEntryType
        {
            Country,
            Other
        }

        public class CoronaEntry : ICoronaEntry
        {
            public CoronaEntryType Type { get; set; }
            public string Name { get; set; }
            public int Cases { get; set; }
            public int Deaths { get; set; }
            public int Recovered { get; set; }
            public int Serious { get; set; }
            public int Active => this.Cases - this.Deaths - this.Recovered;
        }
    }

    internal static class CoronaWatcher
    {
        public static string API_URL = "https://sand-grandiose-draw.glitch.me/";
        public static string ARCHIVE_API_URL = "https://web.archive.org/web/{0}/https://www.worldometers.info/coronavirus/";
        public static CountryList Countries = new CountryList(true);

        public static async void Bind(DiscordSocketClient client)
        {
            //await Task.Delay(TimeSpan.FromMinutes(1));
            while (true)
            {
                try
                {
                    const int GROWTH_OFFSET = 3;
                    var datas = await Task.WhenAll(
                        GetData(API_URL),
                        GetArchiveData(1),
                        GetArchiveData(2),
                        GetArchiveData(GROWTH_OFFSET),
                        GetArchiveData(GROWTH_OFFSET + 1),
                        GetArchiveData(GROWTH_OFFSET + 2));

                    var current = datas[0];
                    var past = datas[1];

                    var regionsNames = new StringBuilder();
                    var regionsActive = new StringBuilder();
                    var growthFactor = new StringBuilder();
                    foreach (var currentRegion in current.Entries.Values.OrderByDescending(e => e.Active).Take(20))
                    {
                        var regions = datas.Select(d =>
                        {
                            d.Entries.TryGetValue(currentRegion.Name, out var res);
                            return res ?? new CoronaData.CoronaEntry();
                        }).ToArray();

                        var currentRegionGrowthFactor = GetGrowthFactor(GROWTH_OFFSET, regions[0], regions[1], regions[3], regions[4]);
                        var pastRegionGrowthFactor = GetGrowthFactor(GROWTH_OFFSET, regions[1], regions[2], regions[4], regions[5]);

                        var pastRegion = regions[1];
                        var cc = GetCountryEmoji(currentRegion);
                        regionsNames.AppendLine(cc + " " + currentRegion.Name);
                        regionsActive.AppendLine(RelativeChangeString(currentRegion.Active, pastRegion.Active));
                        growthFactor.AppendLine(AbsoluteFactorChangeString(currentRegionGrowthFactor, pastRegionGrowthFactor));
                    }

                    var currentGrowthFactor = GetGrowthFactor(GROWTH_OFFSET, datas[0], datas[1], datas[3], datas[4]);
                    var pastGrowthFactor = GetGrowthFactor(GROWTH_OFFSET, datas[1], datas[2], datas[4], datas[5]);

                    var embedBuilder = new EmbedBuilder()
                        .WithTitle("COVID-19 Live Tracker")
                        .WithTimestamp(current.LastUpdated)
                        .WithUrl("https://www.worldometers.info/coronavirus/")

                        .AddField("**Regions Affected**", AbsoluteChangeString(current.RegionsActive, past.RegionsActive), true)
                        .AddField("**Total Cases**", RelativeChangeString(current.Cases, past.Cases), true)
                        .AddField("**Global Growth Factor\\***", AbsoluteFactorChangeString(currentGrowthFactor, pastGrowthFactor), true)

                        .AddField("**Region**", regionsNames.TrimEnd().ToString(), true)
                        .AddField("**Infected**", regionsActive.TrimEnd().ToString(), true)
                        .AddField("**Growth Factor\\***", growthFactor.TrimEnd().ToString(), true)

                        .AddField("**Total Infected**", RelativeChangeString(current.Active, past.Active), true)
                        .AddField("**Total Serious**", RelativeChangeString(current.Serious, past.Serious), true)
                        .AddField("**Total Mild**", RelativeChangeString(current.Mild, past.Mild), true)

                        .AddField("**Total Recovered**", RelativeChangeString(current.Recovered, past.Recovered), true)
                        .AddField("**Total Deaths**", RelativeChangeString(current.Deaths, past.Deaths), true)
                        .AddField("**Global Death Rate**", AbsolutePercentageChangeString(current.DeathRate, past.DeathRate), true)

                        .AddField("**Notes**", "_\\*: Growth factor is the factor by which the number of new cases multiplies itself every day. The average growth factor in the past three days is shown here._")
                        .AddField("**Links**", "[WHO](https://www.who.int/emergencies/diseases/novel-coronavirus-2019/advice-for-public) | [CDC (USA)](https://www.cdc.gov/coronavirus/2019-nCoV/index.html) | [Reddit](https://www.reddit.com/r/Coronavirus/)");

                    var msg = (IUserMessage)await client
                        .GetGuild(DiscordSettings.GuildId)
                        .GetTextChannel(688447767529521332)
                        .GetMessageAsync(688448057121046637);
                    await msg.ModifyAsync(m => m.Embed = embedBuilder.Build());
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    throw;
                }
                
                await Task.Delay(TimeSpan.FromMinutes(2.5));
            }
        }

        public static string GetCountryEmoji(CoronaData.CoronaEntry region)
        {
            var cc = Countries.GetTwoLettersName(region.Name, false);
            if (cc == "") cc = new string(region.Name.Where(c => Char.IsLetter(c)).Take(2).ToArray());
            if (cc == "UK") cc = "GB";
            return $":flag_{cc.ToLowerInvariant()}:";
        }

        public static double GetGrowthFactor(int offset, ICoronaEntry current, ICoronaEntry past, ICoronaEntry past2, ICoronaEntry past3)
        {
            var growthDay = current.Cases - past.Cases;
            var growthPrevious = past2.Cases - past3.Cases;
            var res = ((double)growthDay / growthPrevious - 1) / offset + 1;
            if (Double.IsNaN(res))
                return 0;
            return res;
        }

        public static string AbsoluteChangeString(int current, int past)
        {
            var change = current - past;
            return $"{current}{change: (+0); (-0);#}";
        }

        public static string AbsoluteFactorChangeString(double current, double past)
        {
            var change = current - past;
            if (Double.IsInfinity(change)) change = 0;
            var inf = Double.IsPositiveInfinity(change) ? "∞x" : $"{current:0.00}x";
            return $"{inf}{change: (+0.00); (-0.00);#}";
        }

        public static string RelativeChangeString(int current, int past)
        {
            var nfi = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
            nfi.NumberDecimalDigits = 0;
            nfi.NumberGroupSeparator = " ";

            var change = (double)current / past - 1;
            var inf = Double.IsPositiveInfinity(change) ? " (+∞)" : "";
            if (Double.IsNaN(change) || Double.IsInfinity(change)) change = 0;
            return String.Format(nfi, "{0:N}{1}{2: (+0%); (-0%);#}", current, inf, change);
        }

        public static string AbsolutePercentageChangeString(double current, double past)
        {
            var change = current - past;
            return $"{current:0.0%}{change: (+0.0%); (-0.0%);#}";
        }

        public static double ParseDouble(HtmlNode node)
        {
            Double.TryParse(node.InnerText, NumberStyles.Number, CultureInfo.InvariantCulture, out var num);
            return num;
        }

        public static Task<CoronaData> GetArchiveData(int offset)
        {
            return GetData(String.Format(ARCHIVE_API_URL, DateTime.UtcNow.Subtract(TimeSpan.FromDays(offset)).ToString("yyyyMMddHHmmss")));
        }

        public static async Task<CoronaData> GetData(string url)
        {
            var web = new HtmlWeb();
            var coronaStats = await web.LoadFromWebAsync(url);
            var result = new CoronaData();
            result.LastUpdated = DateTimeOffset.Parse(
                    coronaStats.DocumentNode.SelectNodes("//div")
                    .First(e => e.GetAttributeValue("style", "") == "font-size:13px; color:#999; text-align:center")
                    .InnerText
                    .Substring("Last updated: ".Length)
                ).UtcDateTime;

            result.CaseHistory = Regex.Match(
                    coronaStats.DocumentNode.SelectNodes("//script")
                        .First(e => e.InnerText.Contains("Highcharts.chart('coronavirus-cases-linear'"))
                    .InnerText,
                    @"data: \[([0-9,]+)\]"
                ).Groups[1].Value.Split(',')
                .Select(i => Int32.Parse(i))
                .ToList();


            var countries = coronaStats.DocumentNode.SelectNodes("//*[@id=\"main_table_countries\"]/tbody[1]/tr");
            foreach (var country in countries)
            {
                var entry = new CoronaData.CoronaEntry();
                var cells = country.SelectNodes("td");
                entry.Name = cells[0].InnerText.Trim();
                entry.Type = cells[0].SelectSingleNode("span") == null ? CoronaData.CoronaEntryType.Country : CoronaData.CoronaEntryType.Other;
                entry.Cases = (int)ParseDouble(cells[1]);
                entry.Deaths = (int)ParseDouble(cells[3]);
                entry.Recovered = (int)ParseDouble(cells[5]);
                entry.Serious = (int)ParseDouble(cells[7]);
                result.Entries.Add(entry.Name, entry);
            }
            return result;
        }

        public class CountryList
        {
            private CultureTypes _AllCultures;
            public CountryList(bool AllCultures)
            {
                this._AllCultures = (AllCultures) ? CultureTypes.AllCultures : CultureTypes.SpecificCultures;
                this.Countries = GetAllCountries(this._AllCultures);
            }

            public List<CountryInfo> Countries { get; set; }

            public List<CountryInfo> GetCountryInfoByName(string CountryName, bool NativeName)
            {
                return (NativeName) ? this.Countries.Where(info => info.Region.NativeName == CountryName).ToList()
                                    : this.Countries.Where(info => info.Region.EnglishName == CountryName).ToList();
            }

            public List<CountryInfo> GetCountryInfoByName(string CountryName, bool NativeName, bool IsNeutral)
            {
                return (NativeName) ? this.Countries.Where(info => info.Region.NativeName == CountryName &&
                                                                   info.Culture.IsNeutralCulture == IsNeutral).ToList()
                                    : this.Countries.Where(info => info.Region.EnglishName == CountryName &&
                                                                   info.Culture.IsNeutralCulture == IsNeutral).ToList();
            }

            public string GetTwoLettersName(string CountryName, bool NativeName)
            {
                CountryInfo country = (NativeName) ? this.Countries.Where(info => info.Region.NativeName == CountryName)
                                                                   .FirstOrDefault()
                                                   : this.Countries.Where(info => info.Region.EnglishName == CountryName)
                                                                   .FirstOrDefault();

                return (country != null) ? country.Region.TwoLetterISORegionName : string.Empty;
            }

            public string GetThreeLettersName(string CountryName, bool NativeName)
            {
                CountryInfo country = (NativeName) ? this.Countries.Where(info => info.Region.NativeName.Contains(CountryName))
                                                                    .FirstOrDefault()
                                                   : this.Countries.Where(info => info.Region.EnglishName.Contains(CountryName))
                                                                    .FirstOrDefault();

                return (country != null) ? country.Region.ThreeLetterISORegionName : string.Empty;
            }

            public List<string> GetIetfLanguageTag(string CountryName, bool NativeName)
            {
                return (NativeName) ? this.Countries.Where(info => info.Region.NativeName == CountryName)
                                                    .Select(info => info.Culture.IetfLanguageTag).ToList()
                                    : this.Countries.Where(info => info.Region.EnglishName == CountryName)
                                                    .Select(info => info.Culture.IetfLanguageTag).ToList();
            }

            public List<int> GetRegionGeoId(string CountryName, bool NativeName)
            {
                return (NativeName) ? this.Countries.Where(info => info.Region.NativeName == CountryName)
                                                    .Select(info => info.Region.GeoId).ToList()
                                    : this.Countries.Where(info => info.Region.EnglishName == CountryName)
                                                    .Select(info => info.Region.GeoId).ToList();
            }

            private static List<CountryInfo> GetAllCountries(CultureTypes cultureTypes)
            {
                List<CountryInfo> Countries = new List<CountryInfo>();

                foreach (CultureInfo culture in CultureInfo.GetCultures(cultureTypes))
                {
                    if (culture.LCID != 127)
                        Countries.Add(new CountryInfo()
                        {
                            Culture = culture,
                            Region = new RegionInfo(culture.TextInfo.CultureName)
                        });
                }
                return Countries;
            }

            public class CountryInfo
            {
                public CultureInfo Culture { get; set; }
                public RegionInfo Region { get; set; }
            }
        }
    }
}