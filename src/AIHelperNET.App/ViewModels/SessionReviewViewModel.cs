using System.IO;
using AIHelperNET.Application.Reviews;
using AIHelperNET.Application.Reviews.Commands;
using AIHelperNET.Application.Reviews.Queries;
using AIHelperNET.Domain.Ids;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mediator;
using Microsoft.Win32;
using Serilog;

namespace AIHelperNET.App.ViewModels;

/// <summary>View model for the post-session review window.</summary>
public sealed partial class SessionReviewViewModel(IMediator mediator) : ObservableObject, IDisposable
{
    private SessionId _sessionId;
    private CancellationTokenSource _cts = new();

    [NotifyCanExecuteChangedFor(nameof(RegenerateCommand))]
    [ObservableProperty] private bool   _isLoading;

    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [ObservableProperty] private string _markdown        = string.Empty;
    [ObservableProperty] private string _generatedAtLabel = string.Empty;
    [ObservableProperty] private string _errorMessage    = string.Empty;
    [ObservableProperty] private string _modelUsed       = string.Empty;

    /// <summary>True when <see cref="ErrorMessage"/> is non-empty; drives the error banner visibility.</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    partial void OnErrorMessageChanged(string value) => OnPropertyChanged(nameof(HasError));

    /// <summary>Sets the session this VM will load/generate a review for.
    /// Must be called before <see cref="LoadAsync"/>.</summary>
    public void Initialize(SessionId sessionId) => _sessionId = sessionId;

    /// <summary>Loads an existing review if available; otherwise generates one.</summary>
    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;
        var token = _cts.Token;
        try
        {
            // Try to get a persisted review first
            var getResult = await mediator.Send(new GetSessionReviewQuery(_sessionId), token);
            if (getResult.IsSuccess && getResult.Value is not null)
            {
                ApplyReview(getResult.Value);
                return;
            }

            // No persisted review → generate
            await GenerateCoreAsync(token);
        }
        catch (OperationCanceledException)
        {
            // Silently swallow; user closed the window
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SessionReviewViewModel: unexpected error in LoadAsync");
            ErrorMessage = "Failed to load review: " + ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanRegenerate() => !IsLoading;

    /// <summary>Re-generates the review, replacing the current one.</summary>
    [RelayCommand(CanExecute = nameof(CanRegenerate))]
    public async Task RegenerateAsync()
    {
        if (IsLoading) return; // belt for programmatic calls
        IsLoading = true;
        ErrorMessage = string.Empty;
        var token = _cts.Token;
        try
        {
            await GenerateCoreAsync(token);
        }
        catch (OperationCanceledException)
        {
            // Silently swallow
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SessionReviewViewModel: unexpected error in RegenerateAsync");
            ErrorMessage = "Failed to regenerate review: " + ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task GenerateCoreAsync(CancellationToken token)
    {
        // IsLoading is owned by the caller (LoadAsync / RegenerateAsync)
        var result = await mediator.Send(new GenerateSessionReviewCommand(_sessionId), token);
        if (result.IsSuccess)
            ApplyReview(result.Value);
        else
            ErrorMessage = string.Join(", ", result.Errors.Select(e => e.Message));
    }

    private void ApplyReview(SessionReviewDto dto)
    {
        Markdown         = dto.Markdown;
        ModelUsed        = dto.ModelUsed;
        GeneratedAtLabel = dto.GeneratedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }

    /// <summary>Cancels any in-flight operation. Called by the window on Close.</summary>
    public void Cancel()
    {
        _cts.Cancel();
    }

    private bool CanExport() => !string.IsNullOrEmpty(Markdown);

    /// <summary>Exports the current markdown to a user-chosen file.</summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    public async Task ExportAsync()
    {
        if (string.IsNullOrEmpty(Markdown)) return;

        var dlg = new SaveFileDialog
        {
            Title      = "Export review",
            Filter     = "Markdown (*.md)|*.md|Text (*.txt)|*.txt",
            FileName   = $"review-{_sessionId}",
            DefaultExt = ".md"
        };

        if (dlg.ShowDialog() != true) return;

        await File.WriteAllTextAsync(dlg.FileName, Markdown);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _cts.Dispose();
    }
}
