# Compact interface

The small main window takes its visual direction from the author's [Gaze Lab licence gate](https://github.com/IamAngusU/CS2GazeLab/blob/main/Licensing/LicenseGateWindow.xaml) and [overlay menu](https://github.com/IamAngusU/CS2GazeLab/blob/main/Rendering/OverlayWindow.xaml). It is a normal desktop window; it does not render over or attach to Battlefield.

- Background `#F7F9FB`, main text/action `#15202B`, secondary text `#617181`, status surface `#EEF2F5`, white secondary buttons.
- Segoe UI, a single left-aligned column, status directly after compatibility review, one contextual mutation action.
- English starts every session, with an immediate Deutsch switch. Changing language clears consent. Busy operations lock language, review and mutation controls.
- Apply is replaced by Undo/recovery when a local pending record exists. Both actions have independent consent and use the same guarded recovery engine and elevation worker as before.
- No automatic game scan, mutation or upload on open. Reading the helper's own history makes recovery available. Technical paths stay behind Details; optional folder selection and More tools keep the main surface small.

The interface uses .NET Framework Windows Forms already available on the supported Windows systems. No browser, web server, installer or new runtime is added. The default client is 548 × 650 at 96 DPI, with resizing and scrolling available for smaller layouts. The source is [CompactForm.cs](../src/CompactForm.cs).

Local UI tests cover English on a German culture, language-switch consent reset, Apply/Undo separation, a changed folder invalidating review, busy-state locks and recovery of an interrupted operation. English, German and synthetic recovery views are rendered for visual inspection. These checks do not approve real UAC or modify the player's game.
