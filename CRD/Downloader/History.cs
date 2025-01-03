using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CRD.Downloader.Crunchyroll;
using CRD.Utils;
using CRD.Utils.Sonarr;
using CRD.Utils.Sonarr.Models;
using CRD.Utils.Structs;
using CRD.Utils.Structs.History;
using CRD.Views;
using DynamicData;
using ReactiveUI;

namespace CRD.Downloader;

public class History(){
    private readonly CrunchyrollManager crunInstance = CrunchyrollManager.Instance;

    public async Task<bool> CRUpdateSeries(string seriesId, string? seasonId){
        await crunInstance.CrAuth.RefreshToken(true);

        CrSeriesSearch? parsedSeries = await crunInstance.CrSeries.ParseSeriesById(seriesId, "ja-JP", true);

        if (parsedSeries == null){
            Console.Error.WriteLine("Parse Data Invalid - series is maybe only available with VPN or got deleted");
            return false;
        }

        if (parsedSeries.Data != null){
            foreach (var s in parsedSeries.Data){
                var sId = s.Id;
                if (s.Versions is{ Count: > 0 }){
                    foreach (var sVersion in s.Versions.Where(sVersion => sVersion.Original == true)){
                        if (sVersion.Guid != null){
                            sId = sVersion.Guid;
                        }

                        break;
                    }
                }

                if (!string.IsNullOrEmpty(seasonId) && sId != seasonId) continue;


                var seasonData = await crunInstance.CrSeries.GetSeasonDataById(sId, string.IsNullOrEmpty(crunInstance.CrunOptions.HistoryLang) ? crunInstance.DefaultLocale : crunInstance.CrunOptions.HistoryLang, true);
                if (seasonData.Data is{ Count: > 0 }) await UpdateWithSeasonData(seasonData.Data);
            }


            var historySeries = crunInstance.HistoryList.FirstOrDefault(series => series.SeriesId == seriesId);

            if (historySeries != null){
                MatchHistorySeriesWithSonarr(false);
                await MatchHistoryEpisodesWithSonarr(false, historySeries);
                CfgManager.UpdateHistoryFile();
                return true;
            }
        }

        return false;
    }


    public void SetAsDownloaded(string? seriesId, string? seasonId, string episodeId){
        var historySeries = crunInstance.HistoryList.FirstOrDefault(series => series.SeriesId == seriesId);

        if (historySeries != null){
            var historySeason = historySeries.Seasons.FirstOrDefault(s => s.SeasonId == seasonId);

            if (historySeason != null){
                var historyEpisode = historySeason.EpisodesList.Find(e => e.EpisodeId == episodeId);

                if (historyEpisode != null){
                    historyEpisode.WasDownloaded = true;
                    historySeason.UpdateDownloaded();
                    return;
                }
            }
        }

        MessageBus.Current.SendMessage(new ToastMessage($"Couldn't update download History", ToastType.Warning, 1));
    }

    public HistoryEpisode? GetHistoryEpisode(string? seriesId, string? seasonId, string episodeId){
        var historySeries = crunInstance.HistoryList.FirstOrDefault(series => series.SeriesId == seriesId);

        if (historySeries != null){
            var historySeason = historySeries.Seasons.FirstOrDefault(s => s.SeasonId == seasonId);

            if (historySeason != null){
                var historyEpisode = historySeason.EpisodesList.Find(e => e.EpisodeId == episodeId);

                if (historyEpisode != null){
                    return historyEpisode;
                }
            }
        }

        return null;
    }

    public (HistoryEpisode? historyEpisode, string downloadDirPath) GetHistoryEpisodeWithDownloadDir(string? seriesId, string? seasonId, string episodeId){
        var historySeries = crunInstance.HistoryList.FirstOrDefault(series => series.SeriesId == seriesId);

        var downloadDirPath = "";

        if (historySeries != null){
            var historySeason = historySeries.Seasons.FirstOrDefault(s => s.SeasonId == seasonId);
            if (!string.IsNullOrEmpty(historySeries.SeriesDownloadPath)){
                downloadDirPath = historySeries.SeriesDownloadPath;
            }

            if (historySeason != null){
                var historyEpisode = historySeason.EpisodesList.Find(e => e.EpisodeId == episodeId);
                if (!string.IsNullOrEmpty(historySeason.SeasonDownloadPath)){
                    downloadDirPath = historySeason.SeasonDownloadPath;
                }

                if (historyEpisode != null){
                    return (historyEpisode, downloadDirPath);
                }
            }
        }

        return (null, downloadDirPath);
    }

    public (HistoryEpisode? historyEpisode, List<string> dublist, List<string> sublist, string downloadDirPath, string videoQuality) GetHistoryEpisodeWithDubListAndDownloadDir(string? seriesId, string? seasonId,
        string episodeId){
        var historySeries = crunInstance.HistoryList.FirstOrDefault(series => series.SeriesId == seriesId);

        var downloadDirPath = "";
        var videoQuality = "";
        List<string> dublist =[];
        List<string> sublist =[];

        if (historySeries != null){
            var historySeason = historySeries.Seasons.FirstOrDefault(s => s.SeasonId == seasonId);
            if (historySeries.HistorySeriesDubLangOverride.Count > 0){
                dublist = historySeries.HistorySeriesDubLangOverride;
            }

            if (historySeries.HistorySeriesSoftSubsOverride.Count > 0){
                sublist = historySeries.HistorySeriesSoftSubsOverride;
            }

            if (!string.IsNullOrEmpty(historySeries.SeriesDownloadPath)){
                downloadDirPath = historySeries.SeriesDownloadPath;
            }

            if (!string.IsNullOrEmpty(historySeries.HistorySeriesVideoQualityOverride)){
                videoQuality = historySeries.HistorySeriesVideoQualityOverride;
            }

            if (historySeason != null){
                var historyEpisode = historySeason.EpisodesList.Find(e => e.EpisodeId == episodeId);
                if (historySeason.HistorySeasonDubLangOverride.Count > 0){
                    dublist = historySeason.HistorySeasonDubLangOverride;
                }

                if (historySeason.HistorySeasonSoftSubsOverride.Count > 0){
                    sublist = historySeason.HistorySeasonSoftSubsOverride;
                }

                if (!string.IsNullOrEmpty(historySeason.SeasonDownloadPath)){
                    downloadDirPath = historySeason.SeasonDownloadPath;
                }

                if (!string.IsNullOrEmpty(historySeason.HistorySeasonVideoQualityOverride)){
                    videoQuality = historySeason.HistorySeasonVideoQualityOverride;
                }

                if (historyEpisode != null){
                    return (historyEpisode, dublist, sublist, downloadDirPath, videoQuality);
                }
            }
        }

        return (null, dublist, sublist, downloadDirPath, videoQuality);
    }

    public List<string> GetDubList(string? seriesId, string? seasonId){
        var historySeries = crunInstance.HistoryList.FirstOrDefault(series => series.SeriesId == seriesId);

        List<string> dublist =[];

        if (historySeries != null){
            var historySeason = historySeries.Seasons.FirstOrDefault(s => s.SeasonId == seasonId);
            if (historySeries.HistorySeriesDubLangOverride.Count > 0){
                dublist = historySeries.HistorySeriesDubLangOverride;
            }

            if (historySeason is{ HistorySeasonDubLangOverride.Count: > 0 }){
                dublist = historySeason.HistorySeasonDubLangOverride;
            }
        }

        return dublist;
    }

    public (List<string> sublist, string videoQuality) GetSubList(string? seriesId, string? seasonId){
        var historySeries = crunInstance.HistoryList.FirstOrDefault(series => series.SeriesId == seriesId);

        List<string> sublist =[];
        var videoQuality = "";

        if (historySeries != null){
            var historySeason = historySeries.Seasons.FirstOrDefault(s => s.SeasonId == seasonId);
            if (historySeries.HistorySeriesSoftSubsOverride.Count > 0){
                sublist = historySeries.HistorySeriesSoftSubsOverride;
            }

            if (!string.IsNullOrEmpty(historySeries.HistorySeriesVideoQualityOverride)){
                videoQuality = historySeries.HistorySeriesVideoQualityOverride;
            }

            if (historySeason is{ HistorySeasonSoftSubsOverride.Count: > 0 }){
                sublist = historySeason.HistorySeasonSoftSubsOverride;
            }

            if (historySeason != null && !string.IsNullOrEmpty(historySeason.HistorySeasonVideoQualityOverride)){
                videoQuality = historySeason.HistorySeasonVideoQualityOverride;
            }
        }

        return (sublist, videoQuality);
    }


    public async Task UpdateWithSeasonData(List<CrunchyEpisode>? episodeList, bool skippVersionCheck = true){
        if (episodeList != null){
            if (!skippVersionCheck){
                var episodeVersions = episodeList.First().Versions;
                if (episodeVersions != null){
                    var version = episodeVersions.Find(a => a.Original);
                    if (version.AudioLocale != episodeList.First().AudioLocale){
                        await CRUpdateSeries(episodeList.First().SeriesId, version.SeasonGuid);
                        return;
                    }
                } else{
                    await CRUpdateSeries(episodeList.First().SeriesId, "");
                    return;
                }
            }


            var firstEpisode = episodeList.First();
            var seriesId = firstEpisode.SeriesId;
            var historySeries = crunInstance.HistoryList.FirstOrDefault(series => series.SeriesId == seriesId);
            if (historySeries != null){
                historySeries.HistorySeriesAddDate ??= DateTime.Now;

                await RefreshSeriesData(seriesId, historySeries);

                var historySeason = historySeries.Seasons.FirstOrDefault(s => s.SeasonId == firstEpisode.SeasonId);

                if (historySeason != null){
                    historySeason.SeasonTitle = firstEpisode.SeasonTitle;
                    historySeason.SeasonNum = Helpers.ExtractNumberAfterS(firstEpisode.Identifier) ?? firstEpisode.SeasonNumber + "";
                    historySeason.SpecialSeason = CheckStringForSpecial(firstEpisode.Identifier);
                    foreach (var crunchyEpisode in episodeList){
                        var historyEpisode = historySeason.EpisodesList.Find(e => e.EpisodeId == crunchyEpisode.Id);

                        if (historyEpisode == null){
                            var langList = new List<string>();

                            if (crunchyEpisode.Versions != null){
                                langList.AddRange(crunchyEpisode.Versions.Select(version => version.AudioLocale));
                            } else{
                                langList.Add(crunchyEpisode.AudioLocale);
                            }

                            var newHistoryEpisode = new HistoryEpisode{
                                EpisodeTitle = GetEpisodeTitle(crunchyEpisode),
                                EpisodeDescription = crunchyEpisode.Description,
                                EpisodeId = crunchyEpisode.Id,
                                Episode = crunchyEpisode.Episode,
                                EpisodeSeasonNum = Helpers.ExtractNumberAfterS(firstEpisode.Identifier) ?? firstEpisode.SeasonNumber + "",
                                SpecialEpisode = !int.TryParse(crunchyEpisode.Episode, out _),
                                HistoryEpisodeAvailableDubLang = Languages.SortListByLangList(langList),
                                HistoryEpisodeAvailableSoftSubs = Languages.SortListByLangList(crunchyEpisode.SubtitleLocales),
                                EpisodeCrPremiumAirDate = crunchyEpisode.PremiumAvailableDate
                            };

                            historySeason.EpisodesList.Add(newHistoryEpisode);
                        } else{
                            var langList = new List<string>();

                            if (crunchyEpisode.Versions != null){
                                langList.AddRange(crunchyEpisode.Versions.Select(version => version.AudioLocale));
                            } else{
                                langList.Add(crunchyEpisode.AudioLocale);
                            }

                            //Update existing episode
                            historyEpisode.EpisodeTitle = GetEpisodeTitle(crunchyEpisode);
                            historyEpisode.SpecialEpisode = !int.TryParse(crunchyEpisode.Episode, out _);
                            historyEpisode.EpisodeDescription = crunchyEpisode.Description;
                            historyEpisode.EpisodeId = crunchyEpisode.Id;
                            historyEpisode.Episode = crunchyEpisode.Episode;
                            historyEpisode.EpisodeSeasonNum = Helpers.ExtractNumberAfterS(crunchyEpisode.Identifier) ?? crunchyEpisode.SeasonNumber + "";

                            historyEpisode.HistoryEpisodeAvailableDubLang = Languages.SortListByLangList(langList);
                            historyEpisode.HistoryEpisodeAvailableSoftSubs = Languages.SortListByLangList(crunchyEpisode.SubtitleLocales);
                        }
                    }

                    historySeason.EpisodesList.Sort(new NumericStringPropertyComparer());
                } else{
                    var newSeason = NewHistorySeason(episodeList, firstEpisode);

                    newSeason.EpisodesList.Sort(new NumericStringPropertyComparer());

                    historySeries.Seasons.Add(newSeason);
                    newSeason.Init();
                }

                historySeries.UpdateNewEpisodes();
            } else{
                historySeries = new HistorySeries{
                    SeriesTitle = firstEpisode.SeriesTitle,
                    SeriesId = firstEpisode.SeriesId,
                    Seasons =[],
                    HistorySeriesAddDate = DateTime.Now,
                };
                crunInstance.HistoryList.Add(historySeries);

                var newSeason = NewHistorySeason(episodeList, firstEpisode);

                newSeason.EpisodesList.Sort(new NumericStringPropertyComparer());

                await RefreshSeriesData(seriesId, historySeries);


                historySeries.Seasons.Add(newSeason);
                historySeries.UpdateNewEpisodes();
                historySeries.Init();
                newSeason.Init();
            }

            SortItems();
            SortSeasons(historySeries);
        }
    }

    private CrSeriesBase? cachedSeries;

    private string GetEpisodeTitle(CrunchyEpisode crunchyEpisode){
        if (crunchyEpisode.Identifier.Contains("|M|")){
            if (string.IsNullOrEmpty(crunchyEpisode.Title)){
                if (crunchyEpisode.SeasonTitle.StartsWith(crunchyEpisode.SeriesTitle)){
                    var splitTitle = crunchyEpisode.SeasonTitle.Split(new[]{ crunchyEpisode.SeriesTitle }, StringSplitOptions.None);
                    var titlePart = splitTitle.Length > 1 ? splitTitle[1] : splitTitle[0];
                    var cleanedTitle = Regex.Replace(titlePart, @"^[^a-zA-Z]+", "");

                    return cleanedTitle;
                }

                return crunchyEpisode.SeasonTitle;
            }

            if (crunchyEpisode.Title.StartsWith(crunchyEpisode.SeriesTitle)){
                var splitTitle = crunchyEpisode.Title.Split(new[]{ crunchyEpisode.SeriesTitle }, StringSplitOptions.None);
                var titlePart = splitTitle.Length > 1 ? splitTitle[1] : splitTitle[0];
                var cleanedTitle = Regex.Replace(titlePart, @"^[^a-zA-Z]+", "");

                return cleanedTitle;
            }

            return crunchyEpisode.Title;
        }

        return crunchyEpisode.Title;
    }

    private async Task RefreshSeriesData(string seriesId, HistorySeries historySeries){
        if (cachedSeries == null || (cachedSeries.Data != null && cachedSeries.Data.First().Id != seriesId)){
            cachedSeries = await crunInstance.CrSeries.SeriesById(seriesId, string.IsNullOrEmpty(crunInstance.CrunOptions.HistoryLang) ? crunInstance.DefaultLocale : crunInstance.CrunOptions.HistoryLang, true);
        } else{
            if (cachedSeries?.Data != null){
                var series = cachedSeries.Data.First();
                historySeries.SeriesDescription = series.Description;
                historySeries.ThumbnailImageUrl = GetSeriesThumbnail(cachedSeries);
                historySeries.SeriesTitle = series.Title;
                historySeries.HistorySeriesAvailableDubLang = Languages.SortListByLangList(series.AudioLocales);
                historySeries.HistorySeriesAvailableSoftSubs = Languages.SortListByLangList(series.SubtitleLocales);
            }

            return;
        }

        if (cachedSeries?.Data != null){
            var series = cachedSeries.Data.First();
            historySeries.SeriesDescription = series.Description;
            historySeries.ThumbnailImageUrl = GetSeriesThumbnail(cachedSeries);
            historySeries.SeriesTitle = series.Title;
            historySeries.HistorySeriesAvailableDubLang = Languages.SortListByLangList(series.AudioLocales);
            historySeries.HistorySeriesAvailableSoftSubs = Languages.SortListByLangList(series.SubtitleLocales);
        }
    }

    private void SortSeasons(HistorySeries series){
        var sortedSeasons = series.Seasons
            .OrderBy(s => {
                double seasonNum;
                return double.TryParse(s.SeasonNum, NumberStyles.Any, CultureInfo.InvariantCulture, out seasonNum)
                    ? seasonNum
                    : double.MaxValue;
            })
            .ToList();

        series.Seasons.Clear();

        foreach (var season in sortedSeasons){
            series.Seasons.Add(season);
        }
    }

    public void SortItems(){
        var currentSortingType = CrunchyrollManager.Instance.CrunOptions.HistoryPageProperties?.SelectedSorting ?? SortingType.SeriesTitle;
        var sortingDir = CrunchyrollManager.Instance.CrunOptions.HistoryPageProperties != null && CrunchyrollManager.Instance.CrunOptions.HistoryPageProperties.Ascending;
        DateTime today = DateTime.Now.Date;
        switch (currentSortingType){
            case SortingType.SeriesTitle:
                var sortedList = sortingDir
                    ? CrunchyrollManager.Instance.HistoryList.OrderByDescending(s => s.SeriesTitle).ToList()
                    : CrunchyrollManager.Instance.HistoryList.OrderBy(s => s.SeriesTitle).ToList();

                CrunchyrollManager.Instance.HistoryList.Clear();

                CrunchyrollManager.Instance.HistoryList.AddRange(sortedList);


                return;

            case SortingType.NextAirDate:

                var sortedSeriesDates = sortingDir
                    ? CrunchyrollManager.Instance.HistoryList
                        .OrderByDescending(s => {
                            var date = ParseDate(s.SonarrNextAirDate, today);
                            return date.HasValue ? date.Value : DateTime.MinValue;
                        })
                        .ThenByDescending(s => s.SonarrNextAirDate == "Today" ? 1 : 0)
                        .ThenBy(s => string.IsNullOrEmpty(s.SonarrNextAirDate) ? 1 : 0)
                        .ThenBy(s => s.SeriesTitle)
                        .ToList()
                    : CrunchyrollManager.Instance.HistoryList
                        .OrderByDescending(s => s.SonarrNextAirDate == "Today")
                        .ThenBy(s => s.SonarrNextAirDate == "Today" ? s.SeriesTitle : null)
                        .ThenBy(s => {
                            var date = ParseDate(s.SonarrNextAirDate, today);
                            return date.HasValue ? date.Value : DateTime.MaxValue;
                        })
                        .ThenBy(s => s.SeriesTitle)
                        .ToList();

                CrunchyrollManager.Instance.HistoryList.Clear();

                CrunchyrollManager.Instance.HistoryList.AddRange(sortedSeriesDates);


                return;

            case SortingType.HistorySeriesAddDate:

                var sortedSeriesAddDates = CrunchyrollManager.Instance.HistoryList
                    .OrderBy(s => sortingDir
                        ? -(s.HistorySeriesAddDate?.Date.Ticks ?? DateTime.MinValue.Ticks)
                        : s.HistorySeriesAddDate?.Date.Ticks ?? DateTime.MaxValue.Ticks)
                    .ThenBy(s => s.SeriesTitle)
                    .ToList();


                CrunchyrollManager.Instance.HistoryList.Clear();

                CrunchyrollManager.Instance.HistoryList.AddRange(sortedSeriesAddDates);

                return;
        }
    }

    public DateTime? ParseDate(string dateStr, DateTime today){
        if (dateStr == "Today"){
            return today;
        }

        if (DateTime.TryParseExact(dateStr, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date)){
            return date;
        }

        return null;
    }


    private string GetSeriesThumbnail(CrSeriesBase series){
        // var series = await crunInstance.CrSeries.SeriesById(seriesId);

        if ((series.Data ?? Array.Empty<SeriesBaseItem>()).First().Images.PosterTall?.Count > 0){
            return series.Data.First().Images.PosterTall.First().First(e => e.Height == 360).Source;
        }

        return "";
    }

    private bool CheckStringForSpecial(string identifier){
        if (string.IsNullOrEmpty(identifier)){
            return false;
        }

        // Regex pattern to match any sequence that does NOT contain "|S" followed by one or more digits immediately after
        string pattern = @"^(?!.*\|S\d+).*";

        // Use Regex.IsMatch to check if the identifier matches the pattern
        return Regex.IsMatch(identifier, pattern);
    }

    private HistorySeason NewHistorySeason(List<CrunchyEpisode> seasonData, CrunchyEpisode firstEpisode){
        var newSeason = new HistorySeason{
            SeasonTitle = firstEpisode.SeasonTitle,
            SeasonId = firstEpisode.SeasonId,
            SeasonNum = Helpers.ExtractNumberAfterS(firstEpisode.Identifier) ?? firstEpisode.SeasonNumber + "",
            EpisodesList =[],
            SpecialSeason = CheckStringForSpecial(firstEpisode.Identifier)
        };

        foreach (var crunchyEpisode in seasonData){
            var langList = new List<string>();

            if (crunchyEpisode.Versions != null){
                langList.AddRange(crunchyEpisode.Versions.Select(version => version.AudioLocale));
            } else{
                langList.Add(crunchyEpisode.AudioLocale);
            }

            Languages.SortListByLangList(langList);

            var newHistoryEpisode = new HistoryEpisode{
                EpisodeTitle = GetEpisodeTitle(crunchyEpisode),
                EpisodeDescription = crunchyEpisode.Description,
                EpisodeId = crunchyEpisode.Id,
                Episode = crunchyEpisode.Episode,
                EpisodeSeasonNum = Helpers.ExtractNumberAfterS(firstEpisode.Identifier) ?? firstEpisode.SeasonNumber + "",
                SpecialEpisode = !int.TryParse(crunchyEpisode.Episode, out _),
                HistoryEpisodeAvailableDubLang = langList,
                HistoryEpisodeAvailableSoftSubs = crunchyEpisode.SubtitleLocales,
                EpisodeCrPremiumAirDate = crunchyEpisode.PremiumAvailableDate
            };

            newSeason.EpisodesList.Add(newHistoryEpisode);
        }

        return newSeason;
    }

    public void MatchHistorySeriesWithSonarr(bool updateAll){
        if (crunInstance.CrunOptions.SonarrProperties is{ SonarrEnabled: false }){
            return;
        }

        foreach (var historySeries in crunInstance.HistoryList){
            if (updateAll || string.IsNullOrEmpty(historySeries.SonarrSeriesId)){
                var sonarrSeries = FindClosestMatch(historySeries.SeriesTitle);
                if (sonarrSeries != null){
                    historySeries.SonarrSeriesId = sonarrSeries.Id + "";
                    historySeries.SonarrTvDbId = sonarrSeries.TvdbId + "";
                    historySeries.SonarrSlugTitle = sonarrSeries.TitleSlug;
                }
            }
        }
    }

    private static readonly object _lock = new object();

    public async Task MatchHistoryEpisodesWithSonarr(bool updateAll, HistorySeries historySeries){
        if (crunInstance.CrunOptions.SonarrProperties is{ SonarrEnabled: false }){
            return;
        }

        if (!string.IsNullOrEmpty(historySeries.SonarrSeriesId)){
            List<SonarrEpisode>? episodes = await SonarrClient.Instance.GetEpisodes(int.Parse(historySeries.SonarrSeriesId));

            historySeries.SonarrNextAirDate = GetNextAirDate(episodes);

            List<HistoryEpisode> allHistoryEpisodes =[];

            foreach (var historySeriesSeason in historySeries.Seasons){
                allHistoryEpisodes.AddRange(historySeriesSeason.EpisodesList);
            }

            List<HistoryEpisode> failedEpisodes =[];

            Parallel.ForEach(allHistoryEpisodes, historyEpisode => {
                if (updateAll || string.IsNullOrEmpty(historyEpisode.SonarrEpisodeId)){
                    // Create a copy of the episodes list for each thread
                    var episodesCopy = new List<SonarrEpisode>(episodes);

                    var episode = FindClosestMatchEpisodes(episodesCopy, historyEpisode.EpisodeTitle ?? string.Empty);
                    if (episode != null){
                        historyEpisode.SonarrEpisodeId = episode.Id + "";
                        historyEpisode.SonarrEpisodeNumber = episode.EpisodeNumber + "";
                        historyEpisode.SonarrHasFile = episode.HasFile;
                        historyEpisode.SonarrAbsolutNumber = episode.AbsoluteEpisodeNumber + "";
                        historyEpisode.SonarrSeasonNumber = episode.SeasonNumber + "";
                        lock (_lock){
                            episodes.Remove(episode);
                        }
                    } else{
                        lock (_lock){
                            failedEpisodes.Add(historyEpisode);
                        }
                    }
                }
            });

            Parallel.ForEach(failedEpisodes, historyEpisode => {
                var episode = episodes.Find(ele => {
                    if (ele == null){
                        return false;
                    }

                    var episodeNumberStr = ele.EpisodeNumber.ToString() ?? string.Empty;
                    var seasonNumberStr = ele.SeasonNumber.ToString() ?? string.Empty;

                    return episodeNumberStr == historyEpisode.Episode && seasonNumberStr == historyEpisode.EpisodeSeasonNum;
                });
                if (episode != null){
                    historyEpisode.SonarrEpisodeId = episode.Id + "";
                    historyEpisode.SonarrEpisodeNumber = episode.EpisodeNumber + "";
                    historyEpisode.SonarrHasFile = episode.HasFile;
                    historyEpisode.SonarrAbsolutNumber = episode.AbsoluteEpisodeNumber + "";
                    historyEpisode.SonarrSeasonNumber = episode.SeasonNumber + "";
                    lock (_lock){
                        episodes.Remove(episode);
                    }
                } else{
                    var episode1 = episodes.Find(ele => {
                        if (ele == null){
                            return false;
                        }

                        return !string.IsNullOrEmpty(historyEpisode.EpisodeDescription) && !string.IsNullOrEmpty(ele.Overview) && Helpers.CalculateCosineSimilarity(ele.Overview, historyEpisode.EpisodeDescription) > 0.8;
                    });

                    if (episode1 != null){
                        historyEpisode.SonarrEpisodeId = episode1.Id + "";
                        historyEpisode.SonarrEpisodeNumber = episode1.EpisodeNumber + "";
                        historyEpisode.SonarrHasFile = episode1.HasFile;
                        historyEpisode.SonarrAbsolutNumber = episode1.AbsoluteEpisodeNumber + "";
                        historyEpisode.SonarrSeasonNumber = episode1.SeasonNumber + "";
                        lock (_lock){
                            episodes.Remove(episode1);
                        }
                    } else{
                        var episode2 = episodes.Find(ele => {
                            if (ele == null){
                                return false;
                            }

                            return ele.AbsoluteEpisodeNumber + "" == historyEpisode.Episode;
                        });
                        if (episode2 != null){
                            historyEpisode.SonarrEpisodeId = episode2.Id + "";
                            historyEpisode.SonarrEpisodeNumber = episode2.EpisodeNumber + "";
                            historyEpisode.SonarrHasFile = episode2.HasFile;
                            historyEpisode.SonarrAbsolutNumber = episode2.AbsoluteEpisodeNumber + "";
                            historyEpisode.SonarrSeasonNumber = episode2.SeasonNumber + "";
                            lock (_lock){
                                episodes.Remove(episode2);
                            }
                        } else{
                            Console.Error.WriteLine($"Could not match episode {historyEpisode.EpisodeTitle} to sonarr episode");
                        }
                    }
                }
            });

            CfgManager.UpdateHistoryFile();
        }
    }

    public string GetNextAirDate(List<SonarrEpisode> episodes){
        DateTime today = DateTime.UtcNow.Date;

        // Check if any episode air date matches today
        var todayEpisode = episodes.FirstOrDefault(e => e.AirDateUtc.Date == today);
        if (todayEpisode != null){
            return "Today";
        }

        // Find the next episode date
        var nextEpisode = episodes
            .Where(e => e.AirDateUtc.Date > today)
            .OrderBy(e => e.AirDateUtc.Date)
            .FirstOrDefault();

        if (nextEpisode != null){
            return nextEpisode.AirDateUtc.ToString("dd.MM.yyyy");
        }

        // If no future episode date is found
        return string.Empty;
    }

    private SonarrSeries? FindClosestMatch(string title){
        SonarrSeries? closestMatch = null;
        double highestSimilarity = 0.0;

        Parallel.ForEach(SonarrClient.Instance.SonarrSeries, series => {
            double similarity = CalculateSimilarity(series.Title.ToLower(), title.ToLower());
            if (similarity > highestSimilarity){
                highestSimilarity = similarity;
                closestMatch = series;
            }
        });

        return highestSimilarity < 0.8 ? null : closestMatch;
    }

    public SonarrEpisode? FindClosestMatchEpisodes(List<SonarrEpisode> episodeList, string title){
        SonarrEpisode? closestMatch = null;
        double highestSimilarity = 0.0;
        object lockObject = new object(); // To synchronize access to shared variables

        Parallel.ForEach(episodeList, episode => {
            if (episode != null){
                double similarity = CalculateSimilarity(episode.Title, title);
                lock (lockObject) // Ensure thread-safe access to shared variables
                {
                    if (similarity > highestSimilarity){
                        highestSimilarity = similarity;
                        closestMatch = episode;
                    }
                }
            }
        });

        return highestSimilarity < 0.8 ? null : closestMatch;
    }

    public CrBrowseSeries? FindClosestMatchCrSeries(List<CrBrowseSeries> episodeList, string title){
        CrBrowseSeries? closestMatch = null;
        double highestSimilarity = 0.0;
        object lockObject = new object(); // To synchronize access to shared variables

        Parallel.ForEach(episodeList, episode => {
            if (episode != null){
                double similarity = CalculateSimilarity(episode.Title, title);
                lock (lockObject) // Ensure thread-safe access to shared variables
                {
                    if (similarity > highestSimilarity){
                        highestSimilarity = similarity;
                        closestMatch = episode;
                    }
                }
            }
        });

        return highestSimilarity < 0.8 ? null : closestMatch;
    }

    public double CalculateSimilarity(string source, string target){
        int distance = LevenshteinDistance(source, target);
        return 1.0 - (double)distance / Math.Max(source.Length, target.Length);
    }


    private int LevenshteinDistance(string source, string target){
        if (string.IsNullOrEmpty(source)){
            return string.IsNullOrEmpty(target) ? 0 : target.Length;
        }

        if (string.IsNullOrEmpty(target)){
            return source.Length;
        }

        int n = source.Length;
        int m = target.Length;

        // Use a single array for distances.
        int[] distances = new int[m + 1];

        // Initialize the distance array.
        for (int j = 0; j <= m; j++){
            distances[j] = j;
        }

        for (int i = 1; i <= n; i++){
            int previousDiagonal = distances[0];
            distances[0] = i;

            for (int j = 1; j <= m; j++){
                int previousDistance = distances[j];
                int cost = (target[j - 1] == source[i - 1]) ? 0 : 1;

                distances[j] = Math.Min(
                    Math.Min(distances[j - 1] + 1, distances[j] + 1),
                    previousDiagonal + cost);

                previousDiagonal = previousDistance;
            }
        }

        // The final distance is in the last cell.
        return distances[m];
    }
}

public class NumericStringPropertyComparer : IComparer<HistoryEpisode>{
    public int Compare(HistoryEpisode x, HistoryEpisode y){
        if (double.TryParse(x.Episode, NumberStyles.Any, CultureInfo.InvariantCulture, out double xDouble) &&
            double.TryParse(y.Episode, NumberStyles.Any, CultureInfo.InvariantCulture, out double yDouble)){
            return xDouble.CompareTo(yDouble);
        }

        // Fall back to string comparison if not parseable as doubles
        return string.Compare(x.Episode, y.Episode, StringComparison.Ordinal);
    }
}