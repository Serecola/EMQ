using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Blazorise.Components;
using EMQ.Shared.Core;
using EMQ.Shared.Library.Entities.Concrete;
using EMQ.Shared.Quiz.Entities.Abstract;
using EMQ.Shared.Quiz.Entities.Concrete;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.SignalR.Client;

namespace EMQ.Client.Components;

public partial class AutocompleteAComponent : IAutocompleteComponent
{
    public MyAutocompleteComponent<AutocompleteA> AutocompleteComponent { get; set; } = null!;

    private const string AutocompleteKey = "autocomplete/a.json";

    public AutocompleteA[] AutocompleteData
    {
        get => AutocompleteDataLoader.GetAutocompleteData<AutocompleteA>(AutocompleteKey);
        set => ClientState.AutocompleteData[AutocompleteKey] = value;
    }

    private AutocompleteA[]? _aCombinedFor;
    private string[]? _aCombined;

    /// <summary>
    /// Precomputes (once per dataset) the per-entry combined normalized strings used by multi-term
    /// matching, mirroring the userscript's up-front <c>config.entries</c> build. Rebuilt only when the
    /// underlying dataset reference changes, so keystroke-time search is a zero-allocation scan.
    /// </summary>
    private string[] GetACombined()
    {
        AutocompleteA[] data = AutocompleteData;
        if (!ReferenceEquals(_aCombinedFor, data) || _aCombined is null)
        {
            _aCombined = AutocompleteHelpers.BuildMultiTermCombinedFromNormalizedArray(data,
                d => new[]
                {
                    d.AALatinAliasNormalized, d.AANonLatinAliasNormalized, d.AALatinAliasNormalizedReversed,
                    d.AANonLatinAliasNormalizedReversed,
                });
            _aCombinedFor = data;
        }

        return _aCombined;
    }

    [Parameter]
    public string Placeholder { get; set; } = "";

    [Parameter]
    public bool IsDisabled { get; set; }

    [Parameter]
    public bool IsQuizPage { get; set; }

    private AutocompleteA? _guess;

    [Parameter]
    public AutocompleteA? Guess
    {
        get => _guess;
        set
        {
            if (_guess != value)
            {
                _guess = value;
                GuessChanged.InvokeAsync(value);
            }
        }
    }

    [Parameter]
    public EventCallback<AutocompleteA?> GuessChanged { get; set; }

    private string? _guessLatin;

    [Parameter]
    public string? GuessLatin
    {
        get => _guessLatin;
        set
        {
            if (_guessLatin != value)
            {
                _guessLatin = value;
                GuessLatinChanged.InvokeAsync(value);
            }
        }
    }

    [Parameter]
    public EventCallback<string?> GuessLatinChanged { get; set; }

    [Parameter]
    public Func<Task>? Callback { get; set; }

    [Parameter]
    public GuessKind GuessKind { get; set; }

    private string LastSearch { get; set; } = "";

    private const string ShowAllResultsStr = "[Show all results]";

    protected override Task OnInitializedAsync()
    {
        return AutocompleteDataLoader.EnsureAutocompleteDataAsync(AutocompleteKey,
            () => _client.GetFromJsonAsync<AutocompleteA[]>(AutocompleteKey));
    }

    public void CallStateHasChanged()
    {
        StateHasChanged();
    }

    public string? GetSelectedText() => throw new NotImplementedException();

    public async Task ClearInputField()
    {
#pragma warning disable CS4014
        AutocompleteComponent.Clear(false); // awaiting this causes signalr messages not to be processed in time (???)
#pragma warning restore CS4014
        StateHasChanged();
    }

    public void CallClose()
    {
        AutocompleteComponent.Close();
    }

    public async Task CallFocusAsync()
    {
        await AutocompleteComponent.Focus();
    }

    private static string? OnIconField(AutocompleteA t)
    {
        string? str = t.MainRole switch
        {
            SongArtistRole.Unknown => "assets/text/U.svg",
            SongArtistRole.Vocals => "assets/text/V.svg",
            SongArtistRole.Composer => "assets/text/C.svg",
            SongArtistRole.Arranger => "assets/text/A.svg",
            SongArtistRole.Lyricist => "assets/text/L.svg",
            _ => null
        };

        return str;
    }

    public TValue[] OnSearch<TValue>(string value)
    {
        if (value.StartsWith("id:"))
        {
            string replaced = value.Replace("id:", "");
            if (string.IsNullOrWhiteSpace(replaced))
            {
                return Array.Empty<TValue>();
            }

            return (TValue[])(object)new AutocompleteA[] { new(Convert.ToInt32(replaced), value) };
        }

        string rawValue = value;
        value = value.NormalizeForAutocomplete();
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<TValue>();
        }

        int maxResults = 30;
        if (ClientState.Preferences.AutocompleteMultiTermMatching)
        {
            string[] queryWords = AutocompleteHelpers.GetAutocompleteQueryWords(rawValue);
            if (queryWords.Length >= 2)
            {
                maxResults = 25;
                ReadOnlySpan<string> wordsSpan = queryWords;
                AutocompleteA[] data = AutocompleteData;
                string[] combined = GetACombined();
                var scored = new List<(AutocompleteA Item, int Score, int Index, bool IsMain)>();
                for (int i = 0; i < combined.Length; i++)
                {
                    AutocompleteA d = data[i];
                    // Mirror the userscript's a.json mapping: combine alias, alt alias, and swapped forms.
                    int? score = AutocompleteHelpers.GetMultiTermMatchScore(combined[i], value, wordsSpan);
                    if (score.HasValue)
                    {
                        scored.Add((d, score.Value, i, d.IsMain));
                    }
                }

                var multiRes = scored
                    .OrderByDescending(x => x.Score)
                    .ThenByDescending(x => x.IsMain)
                    .ThenBy(x => x.Index)
                    .DistinctBy(x => x.Item.AId)
                    .Take(maxResults)
                    .Select(x => new KeyValuePair<AutocompleteA, StringMatch>(x.Item, StringMatch.Contains))
                    .ToList();

                return (TValue[])(object)multiRes
                    .Select(x => x.Key)
                    .ToArray();
            }
        }

        if (value == ShowAllResultsStr.NormalizeForAutocomplete())
        {
            maxResults = int.MaxValue;
            value = LastSearch;
        }
        else
        {
            LastSearch = value;
        }

        var valueSpan = value.AsSpan();
        bool hasNonAscii = !Ascii.IsValid(valueSpan);
        var dictLT = new Dictionary<AutocompleteA, StringMatch>();
        var dictNLT = new Dictionary<AutocompleteA, StringMatch>();
        foreach (AutocompleteA d in AutocompleteData)
        {
            var matchLT = d.AALatinAliasNormalized.AsSpan()
                .StartsWithContains(valueSpan, StringComparison.Ordinal);
            if (matchLT > 0)
            {
                dictLT[d] = matchLT;
            }
            else if (d.AALatinAliasNormalizedReversed.AsSpan()
                         .StartsWithContains(valueSpan, StringComparison.Ordinal) > 0)
            {
                dictLT[d] = matchLT;
            }

            if (hasNonAscii)
            {
                var matchNLT = d.AANonLatinAliasNormalized.AsSpan()
                    .StartsWithContains(valueSpan, StringComparison.Ordinal);
                if (matchNLT > 0)
                {
                    dictNLT[d] = matchNLT;
                }
                else if (d.AANonLatinAliasNormalizedReversed.AsSpan()
                             .StartsWithContains(valueSpan, StringComparison.Ordinal) > 0)
                {
                    dictNLT[d] = matchNLT;
                }
            }
        }

        var res = dictLT.Concat(dictNLT)
            .OrderByDescending(x => x.Value).ThenByDescending(x => x.Key.IsMain)
            .DistinctBy(x => x.Key.AId).Take(maxResults).ToList();
        if (res.Count == maxResults)
        {
            res.Add(new KeyValuePair<AutocompleteA, StringMatch>(
                new AutocompleteA() { AALatinAlias = ShowAllResultsStr }, StringMatch.StartsWith));
        }

        return (TValue[])(object)res
            .Select(x => x.Key)
            .ToArray();
    }

    public AutocompleteA? MapValue(AutocompleteA? value)
    {
        string s = value != null ? value.AALatinAlias : AutocompleteComponent.SelectedText;
        if (string.IsNullOrEmpty(Guess?.AALatinAlias) && string.IsNullOrEmpty(s))
        {
            return null;
        }

        return value ?? new AutocompleteA { AALatinAlias = s };
    }

    private async Task OnValueChanged(AutocompleteA? value)
    {
        Guess = MapValue(value);
        GuessLatin = Guess?.AALatinAlias;
        // Console.WriteLine(Guess);
        if (IsQuizPage)
        {
            // todo do this with callback
            await ClientState.Session!.hubConnection!.SendAsync("SendGuessChanged", GuessLatin, GuessKind);
        }

        // todo important fix this everywhere
        if (Callback != null)
        {
            await Callback.Invoke();
        }
    }
}
