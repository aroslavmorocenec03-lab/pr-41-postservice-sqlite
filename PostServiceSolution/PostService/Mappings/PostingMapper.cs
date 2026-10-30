using PostService.Dtos;
using PostService.Models;

namespace PostService.Mappings;

public static class PostingMapper
{
    public static Posting ToPosting(PostingPostDto dto) => new()
    {
        From = dto.From, To = dto.To, Content = dto.Content, DeliveryType = dto.DeliveryType,
        Weight = dto.Weight, Width = dto.Width, Height = dto.Height, Depth = dto.Depth, Value = dto.Value, Description = dto.Description
    };

    public static Posting ToPosting(PostingPutDto dto) => new()
    {
        Id = dto.Id, From = dto.From, To = dto.To, Content = dto.Content, DeliveryType = dto.DeliveryType,
        Weight = dto.Weight, Width = dto.Width, Height = dto.Height, Depth = dto.Depth, Value = dto.Value, Description = dto.Description, Price = dto.Price
    };

    public static PostingGetDto ToPostingGetDto(Posting posting) => new()
    {
        Id = posting.Id, From = posting.From, To = posting.To, Content = posting.Content,
        DeliveryType = posting.DeliveryType, Weight = posting.Weight,
        Width = posting.Width, Height = posting.Height, Depth = posting.Depth,
        Value = posting.Value, Description = posting.Description, Price = posting.Price, CreatedAt = posting.CreatedAt
    };
}
