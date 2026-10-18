using System.ComponentModel;

namespace EMQ.Shared.Quiz.Entities.Concrete;

public enum AnsweringKind
{
    Typing,

    [Description("Typing (No DD)")]
    TypingNoDropdown,

    [Description("Multiple Choice")]
    MultipleChoice,

    Mixed,
}
