namespace PostService.BusinessLogic;

public sealed class PostingValidationException(Dictionary<string, string[]> errors)
    : Exception("Дані відправлення не відповідають правилам сервісу.")
{
    public Dictionary<string, string[]> Errors { get; } = errors;
}
