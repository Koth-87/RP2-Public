using System;
using UnityEngine;
using Verse;

namespace Planets.UI.Dialogs
{
    /// <summary>
    /// Simple modal dialog for integer input with validation.
    /// </summary>
    public class IntegerInputDialog : Window
    {
        private Action<int> onAccept;
        private int value;
        private string buffer;
        private string title;
        private int min;
        private int max;
        
        public override Vector2 InitialSize => new Vector2(280f, 150f);
        
        public IntegerInputDialog(Action<int> onAccept, int defaultValue = 0, string title = null, int min = 0, int max = int.MaxValue)
        {
            this.onAccept = onAccept;
            this.value = defaultValue;
            this.buffer = defaultValue.ToString();
            this.title = title ?? "Enter value:";
            this.min = min;
            this.max = max;
            
            this.forcePause = true;
            this.doCloseX = true;
            this.closeOnClickedOutside = true;
            this.absorbInputAroundWindow = true;
        }
        
        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            
            Rect titleRect = new Rect(0f, 0f, inRect.width, 30f);
            Widgets.Label(titleRect, title);
            
            Rect inputRect = new Rect(0f, 40f, inRect.width, 30f);
            Widgets.TextFieldNumeric<int>(inputRect, ref value, ref buffer, min, max);
            
            float buttonWidth = 100f;
            float buttonHeight = 30f;
            float buttonY = inRect.height - buttonHeight - 10f;
            float spacing = 10f;
            
            Rect cancelRect = new Rect(inRect.width / 2f - buttonWidth - spacing / 2f, buttonY, buttonWidth, buttonHeight);
            if (Widgets.ButtonText(cancelRect, "Cancel"))
            {
                Close();
            }
            
            Rect okRect = new Rect(inRect.width / 2f + spacing / 2f, buttonY, buttonWidth, buttonHeight);
            if (Widgets.ButtonText(okRect, "OK"))
            {
                onAccept?.Invoke(value);
                Close();
            }
        }
        
        public override void OnAcceptKeyPressed()
        {
            onAccept?.Invoke(value);
            base.OnAcceptKeyPressed();
        }
    }
}
