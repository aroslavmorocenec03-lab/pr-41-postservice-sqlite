using PostService.DataAccess;
using PostService.CommonTypes;
using PostService.Models;

namespace PostService.BusinessLogic;

public class PostingService : IPostingService
{
    private readonly IPostingRepository _repository;

    public PostingService(IPostingRepository repository) => _repository = repository;

    public Posting Create(Posting newPosting)
    {
        ArgumentNullException.ThrowIfNull(newPosting);
        Validate(newPosting, validatePrice: false);
        var saved = Copy(newPosting);
        saved.Price = CalculatePrice(saved.Weight, saved.DeliveryType);
        // SQLite зберігає UTC як цілу кількість секунд Unix.
        saved.CreatedAt = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()).UtcDateTime;
        saved.Id = _repository.Create(saved);
        return saved;
    }

    public List<Posting> GetAll() => _repository.GetList();

    public Posting? Find(int postingId) => _repository.GetById(postingId);

    public Posting? Update(int postingId, Posting posting)
    {
        ArgumentNullException.ThrowIfNull(posting);
        var existing = _repository.GetById(postingId);
        if (existing is null) return null;
        if (postingId != posting.Id)
            throw new PostingValidationException(new() { ["Id"] = ["Id у тілі та адресі запиту мають збігатися."] });
        Validate(posting, validatePrice: true);
        var updated = Copy(posting);
        updated.CreatedAt = existing.CreatedAt;
        return _repository.Update(updated) == 0 ? null : updated;
    }

    public int Delete(int postingId) => _repository.Delete(postingId);

    private static float CalculatePrice(float weight, DeliveryType deliveryType)
    {
        // У документі тарифів немає. Навчальне припущення з попередніх робіт.
        (decimal basis, decimal perKg) = deliveryType switch
        {
            DeliveryType.Department => (40m, 10m),
            DeliveryType.Courier => (80m, 15m),
            DeliveryType.ExpressCourier => (120m, 24m),
            _ => throw new ArgumentOutOfRangeException(nameof(deliveryType))
        };
        return (float)Math.Round(basis + perKg * (decimal)weight, 2, MidpointRounding.AwayFromZero);
    }

    private static void Validate(Posting posting, bool validatePrice)
    {
        var errors = new Dictionary<string, string[]>();
        void Text(string field, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) errors[field] = ["Поле обов’язкове."];
        }
        void Positive(string field, float value)
        {
            // Верхня межа ваги також виключає переповнення розрахунку ціни.
            if (!float.IsFinite(value) || value <= 0 || value > 1000000)
                errors[field] = ["Значення має бути від 0 (не включно) до 1000000."];
        }
        Text("From", posting.From); Text("To", posting.To); Text("Content", posting.Content);
        if (!Enum.IsDefined(posting.DeliveryType)) errors["DeliveryType"] = ["Невідомий спосіб доставки."];
        Positive("Weight", posting.Weight);
        if (!float.IsFinite(posting.Width) || posting.Width <= 0) errors["Width"] = ["Ширина має бути додатною."];
        if (!float.IsFinite(posting.Height) || posting.Height <= 0) errors["Height"] = ["Висота має бути додатною."];
        if (!float.IsFinite(posting.Depth) || posting.Depth <= 0) errors["Depth"] = ["Довжина має бути додатною."];
        if (posting.Value is float value && (!float.IsFinite(value) || value < 0)) errors["Value"] = ["Цінність має бути невід’ємною."];
        if (validatePrice && (!float.IsFinite(posting.Price) || posting.Price < 0)) errors["Price"] = ["Ціна має бути невід’ємною."];
        if (errors.Count > 0) throw new PostingValidationException(errors);
    }

    private static Posting Copy(Posting p) => new()
    {
        Id = p.Id, From = p.From, To = p.To, Content = p.Content, DeliveryType = p.DeliveryType,
        Weight = p.Weight, Width = p.Width, Height = p.Height, Depth = p.Depth,
        Value = p.Value, Description = p.Description, Price = p.Price, CreatedAt = p.CreatedAt
    };
}
