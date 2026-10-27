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

public partial class AutocompleteDeveloperComponent : IAutocompleteComponent
{
    public MyAutocompleteComponent<string> AutocompleteComponent { get; set; } = null!;

    private const string AutocompleteKey = "autocomplete/developer.json";

    public AutocompleteMst[] AutocompleteData
    {
        get => AutocompleteDataLoader.GetAutocompleteData<AutocompleteMst>(AutocompleteKey);
        set => ClientState.AutocompleteData[AutocompleteKey] = value;
    }

    private AutocompleteMst[]? _devCombinedFor;
    private string[]? _devCombined;

    /// <summary>
    /// Precomputes (once per dataset) the per-entry combined normalized strings used by multi-term
    /// matching, mirroring the userscript's up-front <c>config.entries</c> build. Rebuilt only when the
    /// underlying dataset reference changes, so keystroke-time search is a zero-allocation scan.
    /// </summary>
    private string[] GetDevCombined()
    {
        AutocompleteMst[] data = AutocompleteData;
        if (!ReferenceEquals(_devCombinedFor, data) || _devCombined is null)
        {
            _devCombined = AutocompleteHelpers.BuildMultiTermCombinedArray(data,
                d => new[]
                {
                    d.MSTLatinTitle, d.MSTNonLatinTitle, d.MSTNonLatinTitleNormalized, d.MSTLatinTitleNormalized,
                });
            _devCombinedFor = data;
        }

        return _devCombined;
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

    [Parameter]
    public Func<Task>? Callback { get; set; }

    public string? GetSelectedText() => AutocompleteComponent.SelectedText;

    protected override Task OnInitializedAsync()
    {
        return AutocompleteDataLoader.EnsureAutocompleteDataAsync(AutocompleteKey,
            () => _client.GetFromJsonAsync<AutocompleteMst[]>(AutocompleteKey));
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

    private TValue[] OnSearch<TValue>(string value)
    {
        string rawValue = value;
        value = value.NormalizeForAutocomplete();
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<TValue>();
        }

        var valueSpan = value.AsSpan();
        bool hasNonAscii = !Ascii.IsValid(valueSpan);
        const int maxResults = 25; // todo
        if (false)
        {
            string[] queryWords = AutocompleteHelpers.GetAutocompleteQueryWords(rawValue);
            if (queryWords.Length >= 2)
            {
                ReadOnlySpan<string> wordsSpan = queryWords;
                AutocompleteMst[] data = AutocompleteData;
                string[] combined = GetDevCombined();
                var scored = new List<(AutocompleteMst Item, int Score, int Index)>();
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
                    .DistinctBy(x => x.Item.MSTLatinTitle)
                    .Take(maxResults)
                    .Select(x => x.Item.MSTLatinTitle)
                    .ToArray();
            }
        }

        var dictLT = new Dictionary<AutocompleteMst, StringMatch>();
        var dictNLT = new Dictionary<AutocompleteMst, StringMatch>();
        foreach (AutocompleteMst d in AutocompleteData)
        {
            var matchLT = d.MSTLatinTitleNormalized.AsSpan()
                .StartsWithContains(valueSpan, StringComparison.Ordinal);
            if (matchLT > 0)
            {
                dictLT[d] = matchLT;
            }

            if (hasNonAscii)
            {
                var matchNLT = d.MSTNonLatinTitleNormalized.AsSpan()
                    .StartsWithContains(valueSpan, StringComparison.Ordinal);
                if (matchNLT > 0)
                {
                    dictNLT[d] = matchNLT;
                }
            }
        }

        return (TValue[])(object)dictLT.Concat(dictNLT)
            .OrderByDescending(x => x.Value)
            .DistinctBy(x => x.Key.MSTLatinTitle)
            .Take(maxResults)
            .Select(x => x.Key.MSTLatinTitle)
            .ToArray();
    }

    private async Task OnValueChanged(string? value)
    {
        string s = value ?? AutocompleteComponent.SelectedText;
        if (string.IsNullOrEmpty(Guess) && string.IsNullOrEmpty(s))
        {
            return;
        }

        Guess = s;
        // Console.WriteLine(Guess);

        if (IsQuizPage)
        {
            // todo do this with callback
            await ClientState.Session!.hubConnection!.SendAsync("SendGuessChanged", Guess, GuessKind.Developer);
        }

        Callback?.Invoke();
    }
}
