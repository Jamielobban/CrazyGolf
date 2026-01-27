public interface IUseable
{
    int Priority { get; }        // higher wins when multiple overlap
    string UsePrompt { get; }    // for debug/prompt UI
    bool Use(Interactor who);    // tap-E action
}
