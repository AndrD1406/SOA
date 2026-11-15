namespace MedStorage.Catalog.Api.Domain.Entities;

public class Drug
{
    public Guid Id { get; private set; }
    public string TradeName { get; private set; } = string.Empty;
    public string ActiveSubstance { get; private set; } = string.Empty;
    public string DosageForm { get; private set; } = string.Empty;
    public int ShelfLifeMonths { get; private set; }
    public bool IsManualEntry { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Required by EF Core
    private Drug() { }

    public static Drug CreateManualEntry(string tradeName, string activeSubstance, string dosageForm, int shelfLifeMonths)
    {
        if (string.IsNullOrWhiteSpace(tradeName))
            throw new ArgumentException("Trade name is required.", nameof(tradeName));
        if (string.IsNullOrWhiteSpace(activeSubstance))
            throw new ArgumentException("Active substance is required.", nameof(activeSubstance));
        if (shelfLifeMonths <= 0)
            throw new ArgumentOutOfRangeException(nameof(shelfLifeMonths), "Shelf life must be positive.");

        return new Drug
        {
            Id = Guid.NewGuid(),
            TradeName = tradeName.Trim(),
            ActiveSubstance = activeSubstance.Trim(),
            DosageForm = dosageForm,
            ShelfLifeMonths = shelfLifeMonths,
            IsManualEntry = true,
            CreatedAt = DateTime.UtcNow
        };
    }
}
