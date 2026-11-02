using System;
using UnityEngine;
using Verse;
using RimWorld;
using Planets.Utilities;

namespace Planets.UI.Dialogs
{
    /// <summary>
    /// Modal dialog for naming a biome profile or per-biome preset before saving, with an optional "Set as default" checkbox.
    /// </summary>
    public class Dialog_RenamePreset : Window
    {
        private string curName;
        private string curDescription;
        private bool setAsDefault;
        private readonly Action<string, string, bool> onConfirm;
        private readonly Func<string, bool> nameExists;
        private string existsCacheName;
        private bool existsCache;

        public override Vector2 InitialSize => new Vector2(420f, 360f);

        public Dialog_RenamePreset(Action<string, string, bool> onConfirm, Func<string, bool> nameExists, string initialName, string initialDescription = "")
        {
            this.onConfirm = onConfirm;
            this.nameExists = nameExists;
            curName = initialName ?? "";
            curDescription = initialDescription ?? "";
            setAsDefault = false;
            doCloseButton = false;
            doCloseX = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 35f), "Planets.SavePresetAs".Translate());
            Text.Font = GameFont.Small;

            if (Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
            {
                TryConfirm();
                Event.current.Use();
            }

            curName = Widgets.TextField(new Rect(0f, 50f, inRect.width, 35f), curName);

            float descY = 92f;
            Widgets.Label(new Rect(0f, descY, 110f, 28f), "Planets.Description".Translate());
            curDescription = Widgets.TextArea(new Rect(110f, descY, inRect.width - 110f, 70f), curDescription);

            float checkY = descY + 80f;
            Widgets.CheckboxLabeled(new Rect(0f, checkY, inRect.width, 28f), "Planets.SetAsDefaultAutoLoad".Translate(), ref setAsDefault);

            if (NameAlreadyExists())
            {
                GUI.color = Color.yellow;
                Widgets.Label(new Rect(0f, checkY + 32f, inRect.width, 28f), "Planets.SaveNameAlreadyExists".Translate());
                GUI.color = Color.white;
            }

            float btnY = inRect.height - 35f;
            float btnWidth = (inRect.width - 10f) / 2f;

            if (Widgets.ButtonText(new Rect(0f, btnY, btnWidth, 35f), "Cancel".Translate()))
                Close();

            if (Widgets.ButtonText(new Rect(inRect.width - btnWidth, btnY, btnWidth, 35f), "Save".Translate()))
                TryConfirm();
        }

        private bool NameAlreadyExists()
        {
            if (nameExists == null || string.IsNullOrEmpty(curName))
                return false;
            if (existsCacheName != curName)
            {
                existsCacheName = curName;
                existsCache = nameExists(curName);
            }
            return existsCache;
        }

        private void TryConfirm()
        {
            if (!PresetFileNames.TryValidate(curName, out string error))
            {
                Messages.Message(error, MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (NameAlreadyExists())
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    $"Overwrite '{curName}'?",
                    () =>
                    {
                        onConfirm(curName, curDescription, setAsDefault);
                        Close();
                    },
                    true));
                return;
            }

            onConfirm(curName, curDescription, setAsDefault);
            Close();
        }
    }
}
