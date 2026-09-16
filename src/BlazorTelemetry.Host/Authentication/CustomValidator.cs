using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Blazor2fa;

public class CustomValidator : ComponentBase
{
    ValidationMessageStore? _messageStore;

    [CascadingParameter]
    public EditContext CurrentEditContext { get; set; } = default!;

    public bool HasError { get; private set; }

    protected override void OnInitialized()
    {
        if (CurrentEditContext == null)
        {
            throw new InvalidOperationException();
        }
        _messageStore = new(CurrentEditContext);
        CurrentEditContext.OnValidationRequested += (s, arg) => _messageStore.Clear();
    }

    public void AddError(string fieldName, string errorMessage)
    {
        _messageStore!.Clear();
        _messageStore!.Add(CurrentEditContext!.Field(fieldName), errorMessage);
        CurrentEditContext!.NotifyValidationStateChanged();
        HasError = true;
    }

    public void DisplayError(string error)
    {
        _messageStore!.Clear();
        _messageStore!.Add(CurrentEditContext!.Field("All"), error);
        NotifyErrors();
    }

    public void DisplayErrors(Dictionary<string, List<string>> errors)
    {
        _messageStore!.Clear();
        foreach (var error in errors)
        {
            _messageStore!.Add(CurrentEditContext!.Field(error.Key), error.Value);
            HasError = true;
        }
        NotifyErrors();
    }

    public void NotifyErrors()
    {
        CurrentEditContext!.NotifyValidationStateChanged();
    }

    public void Reset()
    {
        _messageStore!.Clear();
        HasError = false;
    }
}
