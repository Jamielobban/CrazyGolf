public interface IInventoryItemSource
{
    int Priority { get; }
    string TakePrompt { get; }
    bool Take(Interactor who);
}
