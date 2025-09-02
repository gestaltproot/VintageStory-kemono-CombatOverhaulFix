using Vintagestory.API.Common.Entities;

namespace kemono;

/// <summary>
/// Dummy entity for attaching clothing to so clothing displays correctly
/// in inventory and guis.
/// </summary>
public class EntityKemonoClothingAttach : Entity
{
    public static string NAME { get; } = "EntityKemonoClothingAttach";

    public override bool StoreWithChunk
    {
        get { return false; }
    }
}
