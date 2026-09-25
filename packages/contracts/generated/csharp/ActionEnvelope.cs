using System.Text.Json;
using System.Text.Json.Serialization;

namespace Autobots.Contracts;

[JsonConverter(typeof(PointerButtonConverter))]
public enum PointerButton
{
    Left,
    Middle,
    Right
}

public sealed class PointerButtonConverter : JsonStringEnumConverter<PointerButton>
{
    public PointerButtonConverter() : base(JsonNamingPolicy.CamelCase) { }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ClickAction), "click")]
[JsonDerivedType(typeof(TypeTextAction), "type_text")]
[JsonDerivedType(typeof(KeyPressAction), "key_press")]
[JsonDerivedType(typeof(ScrollAction), "scroll")]
[JsonDerivedType(typeof(WaitAction), "wait")]
public abstract record ProposedAction;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ClickAction(
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("button")] PointerButton Button) : ProposedAction;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TypeTextAction([property: JsonPropertyName("text")] string Text) : ProposedAction;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record KeyPressAction([property: JsonPropertyName("key")] string Key) : ProposedAction;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScrollAction(
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("delta_x")] int DeltaX,
    [property: JsonPropertyName("delta_y")] int DeltaY) : ProposedAction;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WaitAction([property: JsonPropertyName("duration_ms")] int DurationMs) : ProposedAction;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ActionEnvelope(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("task_id")] Guid TaskId,
    [property: JsonPropertyName("device_id")] Guid DeviceId,
    [property: JsonPropertyName("action_id")] Guid ActionId,
    [property: JsonPropertyName("observation_id")] Guid ObservationId,
    [property: JsonPropertyName("lease_id")] Guid LeaseId,
    [property: JsonPropertyName("epoch")] long Epoch,
    [property: JsonPropertyName("sequence")] long Sequence,
    [property: JsonPropertyName("action")] ProposedAction Action);
