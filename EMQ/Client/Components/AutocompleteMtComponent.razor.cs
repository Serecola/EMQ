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

public partial class AutocompleteMtComponent : IAutocompleteComponent
{
    public MyAutocompleteComponent<AutocompleteMt> AutocompleteComponent { get; set; } = null!;

    private string AutocompleteKey => UseBGM ? "autocomplete/mt_all.json" : "autocomplete/mt.json";

    public AutocompleteMt[] AutocompleteData
    {
        get => AutocompleteDataLoader.GetAutocompleteData<AutocompleteMt>(AutocompleteKey);
        set => ClientState.AutocompleteData[AutocompleteKey] = value;
    }

    private AutocompleteMt[]? _mtCombinedFor;
    private string[]? _mtCombined;

    /// <summary>
    /// Precomputes (once per dataset) the per-entry combined normalized strings used by multi-term
    /// matching, mirroring the userscript's up-front <c>config.entries</c> build. Rebuilt only when the
    /// underlying dataset reference changes, so keystroke-time search is a zero-allocation scan.
    /// </summary>
    private string[] GetMtCombined()
    {
        AutocompleteMt[] data = AutocompleteData;
        if (!ReferenceEquals(_mtCombinedFor, data) || _mtCombined is null)
        {
            _mtCombined = AutocompleteHelpers.BuildMultiTermCombinedFromNormalizedArray(data,
                d => new[] { d.MTLatinTitleNormalized, d.MTNonLatinTitleNormalized });
            _mtCombinedFor = data;
        }

        return _mtCombined;
    }

    [Parameter]
    public string Placeholder { get; set; } = "";

    [Parameter]
    public bool IsDisabled { get; set; }

    [Parameter]
    public bool IsQuizPage { get; set; }

    private string? _guess;

    [Parameter]
    public string? Guess
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
    public EventCallback<string> GuessChanged { get; set; }

    private AutocompleteMt? _guessT;

    [Parameter]
    public AutocompleteMt? GuessT
    {
        get => _guessT;
        set
        {
            if (_guessT != value)
            {
                _guessT = value;
                GuessTChanged.InvokeAsync(value);
            }
        }
    }

    [Parameter]
    public EventCallback<AutocompleteMt> GuessTChanged { get; set; }

    [Parameter]
    public Func<Task>? Callback { get; set; }

    [Parameter]
    public bool UseBGM { get; set; }

    public string? GetSelectedText() => AutocompleteComponent.SelectedText;

    protected override Task OnParametersSetAsync()
    {
        string key = AutocompleteKey;
        return AutocompleteDataLoader.EnsureAutocompleteDataAsync(key,
            () => _client.GetFromJsonAsync<AutocompleteMt[]>(key));
    }

    public void CallStateHasChanged()
    {
        StateHasChanged();
    }

    public async Task ClearInputField()
    {
#pragma warning disable CS4014
        AutocompleteComponent.Clear(false); // awaiting this causes signalr messages not to be processed in time (???)
#pragma warning restore CS4014
        GuessT = null;
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

    private static string? OnIconField(AutocompleteMt t)
    {
        string? str = t.IsBGM switch
        {
            true => "assets/text/B.svg",
            false => "assets/text/V.svg",
            _ => null
        };

        return str;
    }

    private TValue[] OnSearch<TValue>(string value)
    {
        if (value.StartsWith("id:"))
        {
            string replaced = value.Replace("id:", "");
            if (string.IsNullOrWhiteSpace(replaced))
            {
                return Array.Empty<TValue>();
            }

            return (TValue[])(object)new AutocompleteMt[] { new(Convert.ToInt32(replaced), value) };
        }

        string rawValue = value;
        value = value.NormalizeForAutocomplete();
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<TValue>();
        }

        const int maxResults = 25; // todo
        if (false)
        {
            string[] queryWords = AutocompleteHelpers.GetAutocompleteQueryWords(rawValue);
            if (queryWords.Length >= 2)
            {
                ReadOnlySpan<string> wordsSpan = queryWords;
                AutocompleteMt[] data = AutocompleteData;
                string[] combined = GetMtCombined();
                var scored = new List<(AutocompleteMt Item, int Score, int Index)>();
                for (int i = 0; i < combined.Length; i++)
                {
                    int? score = AutocompleteHelpers.GetMultiTermMatchScore(combined[i], value, wordsSpan);
                    if (score.HasValue)
                    {
                        scored.Add((data[i], score.Value, i));
                    }
                }

                return (TValue[])(object)scored
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.Index)
                    .DistinctBy(x => x.Item.MTLatinTitleNormalized)
                    .Take(maxResults)
                    .Select(x => x.Item)
                    .ToArray();
            }
        }

        var valueSpan = value.AsSpan();
        bool hasNonAscii = !Ascii.IsValid(valueSpan);
        var dictLT = new Dictionary<AutocompleteMt, StringMatch>();
        var dictNLT = new Dictionary<AutocompleteMt, StringMatch>();
        foreach (AutocompleteMt d in AutocompleteData)
        {
            var matchLT = d.MTLatinTitleNormalized.AsSpan().StartsWithContains(valueSpan, StringComparison.Ordinal);
            if (matchLT > 0)
            {
                dictLT[d] = matchLT;
            }

            if (hasNonAscii)
            {
                var matchNLT = d.MTNonLatinTitleNormalized.AsSpan().StartsWithContains(value, StringComparison.Ordinal);
                if (matchNLT > 0)
                {
                    dictNLT[d] = matchNLT;
                }
            }
        }

        return (TValue[])(object)dictLT.Concat(dictNLT)
            .OrderByDescending(x => x.Value)
            .DistinctBy(x => x.Key.MTLatinTitleNormalized)
            .Take(maxResults)
            .Select(x => x.Key)
            .ToArray();
    }

    private async Task OnValueChanged(AutocompleteMt? value)
    {
        string s = value?.MTLatinTitle ?? AutocompleteComponent.SelectedText;
        if (string.IsNullOrEmpty(Guess) && string.IsNullOrEmpty(s))
        {
            return;
        }

        Guess = s;
        GuessT = value;
        // Console.WriteLine(Guess);

        if (IsQuizPage)
        {
            // todo do this with callback
            await ClientState.Session!.hubConnection!.SendAsync("SendGuessChanged", Guess, GuessKind.Mt);
        }

        Callback?.Invoke();
    }
}
