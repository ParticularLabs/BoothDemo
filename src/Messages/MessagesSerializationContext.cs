using System.Text.Json.Serialization;

namespace Messages;

[JsonSerializable(typeof(OrderPlaced))]
[JsonSerializable(typeof(OrderBilled))]
[JsonSerializable(typeof(PlaceOrder))]
[JsonSerializable(typeof(OrderShipped))]
[JsonSerializable(typeof(RaffleOrderCompleted))]
[JsonSerializable(typeof(RaffleWinnerSelected))]
public partial class MessagesSerializationContext : JsonSerializerContext;