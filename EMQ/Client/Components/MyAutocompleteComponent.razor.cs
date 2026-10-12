using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EMQ.Shared.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace EMQ.Client.Components;

public partial class MyAutocompleteComponent<TValue> : IDisposable where TValue : notnull
{
    public string SelectedText { get; private set; } = "";

    public TValue? SelectedValue { get; private set; }

    public TValue[] CurrentSearchResults { get; private set; } = Array.Empty<TValue>();

    public bool PreventDefault { get; private set; }

    public bool ShowDropdown { get; private set; }

    public ElementReference InputRef { get; private set; }

    public int CurrentFocus { get; private set; } = -1;

    public bool Answered { get; private set; }

    // todo implement
    [Parameter]
    public int MinLength { get; set; } = 1;

    [Parameter]
    public string? MaxMenuHeight { get; set; }

    [Parameter]
    public string? Placeholder { get; set; }

    [Parameter]
    public bool Disabled { get; set; }

    [Parameter]
    public bool HighlightMatch { get; set; } = true;

    [Parameter]
    public bool RequireConfirmation { get; set; }

    [EditorRequired]
    [Parameter]
    public Func<string, TValue[]> OnSearch { get; set; } = null!;

    [EditorRequired]
    [Parameter]
    public Func<TValue, string> TextField { get; set; } = null!;

    [Parameter]
    public Func<TValue, string>? TextFieldForSelectedText { get; set; }

    [EditorRequired]
    [Parameter]
    public EventCallback<TValue?> OnValueChanged { get; set; }

    [Parameter]
    public string? GuessStr { get; set; }

    [Parameter]
    public Func<TValue, string?>? IconField { get; set; }

    [Parameter]
    public bool ShowIcons { get; set; } = true;

    private CancellationTokenSource? _autoSubmitCts;

    private const int AutoSubmitDebounceMs = 50;

    private bool _isComposing;

    public void Close()
    {
        ShowDropdown = false;
        CurrentFocus = -1;
        StateHasChanged();
    }

    public async Task Focus(bool preventScroll = false)
    {
        await InputRef.FocusAsync(preventScroll);
    }

    public async Task Clear(bool raiseValueChanged)
    {
        SelectedText = "";
        SelectedValue = default;
        CurrentFocus = -1;
        CurrentSearchResults = Array.Empty<TValue>();
        Answered = false;
        StateHasChanged();

        // todo? remove because it's unused
        if (raiseValueChanged)
        {
            await OnValueChanged.InvokeAsync(SelectedValue);
        }
    }

    public Task OnSetInputSearch(string value)
    {
        SelectedText = value;
        CurrentSearchResults = OnSearch.Invoke(value);
        CurrentFocus = Math.Clamp(CurrentFocus, -1,
            CurrentSearchResults.Length == 0 ? -1 : CurrentSearchResults.Length - 1);
        ShowDropdown = true;

        ScheduleInputAutoSubmit();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _autoSubmitCts?.Cancel();
        _autoSubmitCts?.Dispose();
    }

    // Auto-submit on every keystroke (typed character, backspace, delete),
    // mirroring the emq-nodropdown userscript behavior: submit whatever is
    // currently typed as a guess without requiring Enter. Does not hide the
    // dropdown and does not change arrow-key navigation.
    private void ScheduleInputAutoSubmit()
    {
        _autoSubmitCts?.Cancel();
        _autoSubmitCts?.Dispose();

        if (!ClientState.Preferences.AutocompleteInputAutoSubmit)
        {
            _autoSubmitCts = null;
            return;
        }

        if (_isComposing)
        {
            _autoSubmitCts = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedText))
        {
            _autoSubmitCts = null;
            return;
        }

        var cts = new CancellationTokenSource();
        _autoSubmitCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(AutoSubmitDebounceMs, cts.Token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            await InvokeAsync(async () =>
            {
                if (cts.IsCancellationRequested)
                {
                    return;
                }

                if (!ClientState.Preferences.AutocompleteInputAutoSubmit)
                {
                    return;
                }

                if (_isComposing)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(SelectedText))
                {
                    return;
                }

                await AutoSubmitInputValue();
            });
        });
    }

    private async Task AutoSubmitInputValue()
    {
        // Submit the raw typed text (like pressing Enter with no suggestion
        // highlighted) instead of auto-picking the focused dropdown item.
        int savedFocus = CurrentFocus;
        CurrentFocus = -1;
        try
        {
            await SelectValue();
        }
        finally
        {
            // Keep the suggestion list usable while auto-submitting each keystroke.
            // RequireConfirmation manages its own dropdown visibility, so leave it alone there.
            if (ClientState.Preferences.AutocompleteIsEnabled && !RequireConfirmation)
            {
                ShowDropdown = true;
                CurrentFocus = Math.Clamp(savedFocus, -1,
                    CurrentSearchResults.Length == 0 ? -1 : CurrentSearchResults.Length - 1);
                StateHasChanged();
            }
        }
    }

    private async Task OnKeyDown(KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "ArrowUp" when ClientState.Preferences.AutocompleteIsEnabled:
                {
                    PreventDefault = true; // prevent the input box cursor jumping to start
                    ShowDropdown = true;
                    CurrentFocus = Math.Clamp(CurrentFocus - 1, 0,
                        CurrentSearchResults.Length == 0 ? 0 : CurrentSearchResults.Length - 1);
                    await ScrollItemIntoView(CurrentFocus);
                    break;
                }
            case "ArrowDown" when ClientState.Preferences.AutocompleteIsEnabled:
                {
                    PreventDefault = true; // prevent the input box cursor jumping to end
                    ShowDropdown = true;
                    CurrentFocus = Math.Clamp(CurrentFocus + 1, 0,
                        CurrentSearchResults.Length == 0 ? 0 : CurrentSearchResults.Length - 1);
                    await ScrollItemIntoView(CurrentFocus);
                    break;
                }
            case "Enter":
            case "NumpadEnter":
                {
                    await SelectValue();
                    break;
                }
            case "Escape":
                {
                    Close();
                    break;
                }
            default:
                {
                    PreventDefault = false;
                    break;
                }
        }
    }

    private async Task ScrollItemIntoView(int index)
    {
        if (!ShowDropdown || index < 0)
        {
            return;
        }

        string elementId = $"autocomplete-item-{index}"; // todo important this is broken for non-mst
        await _jsRuntime.InvokeVoidAsync("scrollElementIntoView", elementId, true);
    }

    private async Task SelectValue()
    {
        TValue? value = CurrentSearchResults.ElementAtOrDefault(CurrentFocus);
        if (value != null)
        {
            string textField = TextField.Invoke(value);
            string textFieldForSelectedText =
                TextFieldForSelectedText != null ? TextFieldForSelectedText.Invoke(value) : textField;
            bool confirmed = !RequireConfirmation || SelectedText == textFieldForSelectedText;
            SelectedText = textFieldForSelectedText;

            if (confirmed)
            {
                SelectedValue = value;
                CurrentSearchResults = OnSearch.Invoke(SelectedText); // todo? option to not do this
                await OnValueChanged.InvokeAsync(SelectedValue);
                Answered = true;
            }
        }
        else
        {
            SelectedValue = default;
            CurrentSearchResults = OnSearch.Invoke(SelectedText); // todo? option to not do this
            await OnValueChanged.InvokeAsync(SelectedValue);
            Answered = true;
        }

        if (RequireConfirmation)
        {
            ShowDropdown = false;
        }
        else
        {
            Close();
        }

        if (CurrentSearchResults.Length > 30) // todo... (must be kept in sync with the one in AutocompleteAComponent)
        {
            ShowDropdown = true;
        }
    }

    private void OnFocus(FocusEventArgs obj)
    {
        ShowDropdown = true;
    }

    private void OnCompositionStart(EventArgs _)
    {
        _isComposing = true;
        _autoSubmitCts?.Cancel();
        _autoSubmitCts?.Dispose();
        _autoSubmitCts = null;
    }

    private void OnCompositionEnd(EventArgs _)
    {
        _isComposing = false;
        ScheduleInputAutoSubmit();
    }

    private void OnBlur(FocusEventArgs obj)
    {
        Close();
    }

    private async Task Onclick_AutocompleteItem(MouseEventArgs obj, int index)
    {
        CurrentFocus = index;
        await SelectValue();
    }

    private void Onmousedown()
    {
        // Do not remove.
    }
}
