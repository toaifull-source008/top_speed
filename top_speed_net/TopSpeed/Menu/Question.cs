using System;
using System.Collections.Generic;

namespace TopSpeed.Menu
{
    internal static class QuestionId
    {
        public const int None = 0;
        public const int Ok = 1;
        public const int Yes = 2;
        public const int No = 3;
        public const int Cancel = 4;
        public const int Confirm = 5;
        public const int Close = 6;
    }

    [Flags]
    internal enum QuestionButtonFlags
    {
        None = 0,
        Default = 1
    }

    internal sealed class QuestionButton
    {
        public QuestionButton(int id, string text, Action? onClick = null, QuestionButtonFlags flags = QuestionButtonFlags.None)
        {
            Id = id;
            Text = text ?? string.Empty;
            OnClick = onClick;
            Flags = flags;
        }

        public QuestionButton(string text, Action onClick, QuestionButtonFlags flags = QuestionButtonFlags.None)
        {
            Id = QuestionId.None;
            Text = text ?? string.Empty;
            OnClick = onClick ?? throw new ArgumentNullException(nameof(onClick));
            Flags = flags;
        }

        public int Id { get; }
        public string Text { get; }
        public Action? OnClick { get; }
        public QuestionButtonFlags Flags { get; }
    }

    internal sealed class Question
    {
        public Question(string title, string caption, Action<int> onResult, params QuestionButton[] buttons)
            : this(title, caption, QuestionId.Cancel, onResult, buttons)
        {
        }

        public Question(string title, string caption, int closeResultId, Action<int> onResult, params QuestionButton[] buttons)
        {
            Title = title ?? string.Empty;
            Caption = caption ?? string.Empty;
            CloseResultId = closeResultId;
            OnResult = onResult ?? throw new ArgumentNullException(nameof(onResult));
            Buttons = buttons ?? Array.Empty<QuestionButton>();
        }

        public Question(string title, string caption, params QuestionButton[] buttons)
        {
            Title = title ?? string.Empty;
            Caption = caption ?? string.Empty;
            CloseResultId = QuestionId.Cancel;
            Buttons = buttons ?? Array.Empty<QuestionButton>();
        }

        public string Title { get; }
        public string Caption { get; }
        public int CloseResultId { get; }
        public Action<int>? OnResult { get; }
        public IReadOnlyList<QuestionButton> Buttons { get; }
    }

    internal sealed class QuestionDialog
    {
        private const string MenuId = "question_dialog";
        private readonly MenuManager _menu;
        private Question? _activeQuestion;

        public QuestionDialog(MenuManager menu)
        {
            _menu = menu ?? throw new ArgumentNullException(nameof(menu));
            _menu.Register(_menu.CreateMenu(MenuId, new[] { new MenuItem("Question", MenuAction.None) }, string.Empty));
            _menu.SetCloseHandler(MenuId, HandleQuestionClose);
        }

        public bool IsQuestionMenu(string? currentMenuId)
        {
            return string.Equals(currentMenuId, MenuId, StringComparison.Ordinal);
        }

        public void Show(Question question)
        {
            if (question == null)
                throw new ArgumentNullException(nameof(question));

            _activeQuestion = question;
            var items = new List<MenuItem>
            {
                new MenuItem(question.Title, MenuAction.None),
                new MenuItem(question.Caption, MenuAction.None)
            };

            var defaultIndex = 2;
            var firstDefaultFound = false;
            for (var i = 0; i < question.Buttons.Count; i++)
            {
                var button = question.Buttons[i];
                if (!firstDefaultFound && (button.Flags & QuestionButtonFlags.Default) != 0)
                {
                    defaultIndex = 2 + i;
                    firstDefaultFound = true;
                }

                var buttonCopy = button;
                items.Add(new MenuItem(button.Text, MenuAction.None, onActivate: () => Complete(question, buttonCopy.Id, buttonCopy.OnClick)));
            }

            _menu.UpdateItems(MenuId, items);
            var announcement = $"{question.Title}  dialog  {question.Caption}";
            _menu.Push(MenuId, announcement, defaultIndex);
        }

        private bool HandleQuestionClose(MenuCloseSource _)
        {
            if (_activeQuestion == null)
                return false;

            Complete(_activeQuestion, _activeQuestion.CloseResultId, null);
            return true;
        }

        private void Complete(Question question, int resultId, Action? buttonAction)
        {
            if (!ReferenceEquals(_activeQuestion, question))
                return;

            _activeQuestion = null;

            if (IsQuestionMenu(_menu.CurrentId) && _menu.CanPop)
                _menu.PopToPrevious();

            question.OnResult?.Invoke(resultId);
            buttonAction?.Invoke();
        }
    }
}
