using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CRD.Downloader.Crunchyroll;
using CRD.Utils;
using CRD.Utils.Structs.History;

namespace CRD.Utils.UI;

public class UiHistoryDownloadStatusConverter : IMultiValueConverter{
    public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture){
        var episode = values.Count > 0 && values[0] is HistoryEpisode ep ? ep : null;
        var series = values.Count > 1 && values[1] is HistorySeries hs ? hs : null;
        var season = values.Count > 2 && values[2] is HistorySeason hsn ? hsn : null;

        var isTooltip = string.Equals(parameter?.ToString(), "Tooltip", StringComparison.OrdinalIgnoreCase);

        if (episode == null || !episode.WasDownloaded){
            return isTooltip ? "Mark as downloaded" : Brushes.Gray;
        }

        var requestedDubs = Enumerable.Empty<string>();
        var requestedSoftSubs = Enumerable.Empty<string>();
        var isPartial = false;
        if (CrunchyrollManager.Instance.CrunOptions.HistoryCheckPartialDownloads){
            requestedDubs = HistorySeries.GetEffectiveDubLang(series, season);
            requestedSoftSubs = HistorySeries.GetEffectiveSoftSubs(series, season, episode);
            isPartial = episode.IsPartiallyDownloaded(requestedDubs, requestedSoftSubs);
        }

        if (isTooltip){
            return BuildTooltip(episode, requestedDubs, requestedSoftSubs, isPartial);
        }

        return isPartial ? new SolidColorBrush(Color.Parse("#f78c25")) : new SolidColorBrush(Color.Parse("#21a556"));
    }

    public IList<object> ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();

    private static string BuildTooltip(
        HistoryEpisode episode,
        IEnumerable<string> requestedDubs,
        IEnumerable<string> requestedSoftSubs,
        bool isPartial){
        if (!isPartial){
            return "Downloaded";
        }

        var missingDubs = GetMissingLocales(
            requestedDubs,
            episode.DownloadedDubLang,
            episode.HistoryEpisodeAvailableDubLang);
        var missingSubs = GetMissingLocales(
            requestedSoftSubs,
            episode.DownloadedSoftSubs,
            episode.HistoryEpisodeAvailableSoftSubs);
        var lines = new List<string>{ "Downloaded, but missing:" };

        if (missingDubs.Count > 0){
            lines.Add($"Dubs: {string.Join(", ", missingDubs)}");
        }

        if (missingSubs.Count > 0){
            lines.Add($"Subs: {string.Join(", ", missingSubs)}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static List<string> GetMissingLocales(
        IEnumerable<string> requested,
        IEnumerable<string> downloaded,
        IEnumerable<string> available){
        var requestedList = requested
            .Where(locale => !string.IsNullOrWhiteSpace(locale))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requestedList.Count == 0 ||
            requestedList.Contains("none", StringComparer.OrdinalIgnoreCase)){
            return [];
        }

        var downloadedSet = downloaded.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var availableList = available
            .Where(locale => !string.IsNullOrWhiteSpace(locale))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var requestedAvailableLocales = requestedList.Contains("all", StringComparer.OrdinalIgnoreCase)
            ? availableList
            : requestedList.Where(locale => availableList.Contains(locale, StringComparer.OrdinalIgnoreCase));

        return requestedAvailableLocales
            .Where(locale => !downloadedSet.Contains(locale))
            .OrderBy(locale => locale, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
