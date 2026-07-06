# Viewing category colours in Outlook

ContinuMail Converter carries your Thunderbird tag colours (and calendar/task category colours)
straight into the converted PST. This page explains how that works, the one real limitation in
how classic Outlook renders it, and the steps to see the colours.

## What

Every PST the converter writes has its **master category list baked in** — an
`IPM.Configuration.CategoryList` associated (hidden) item stored in a top-level "Calendar" folder
inside the PST. That list carries each category's name and its colour (your Thunderbird tag
colour, or Thunderbird's own computed default colour for calendar/task categories that don't have
one). The individual mail, calendar, and task items in the PST already carry the category
**names** as their `Keywords` property; the baked-in list is what tells Outlook which **colour**
each name maps to.

There is no separate "import colours" step, no Outlook automation, and no COM. The colours are
part of the PST file itself, the same as any other converted data — nothing extra to run, nothing
extra to install.

## The limitation

Classic Outlook only reads the category **master list** (the name → colour mapping) from your
**default/primary data store**. It does not look at a secondary store's category list, even
though items in that secondary store still show their category **name**.

Concretely:

- Open the converted PST as your Outlook profile's **primary/default** data store → categorized
  items show their name **and** colour, as expected.
- Open the same PST as a **secondary** data file (`File → Open & Export → Open Outlook Data
  File…`, added alongside an existing mailbox) → categorized items still show the category
  **name**, but with **no colour** (or a generic/none-colour swatch). This is a property of how
  classic Outlook resolves category colours, not a defect in the converted file — the colour data
  is present in the PST either way.

## How to see the colours

To see the colours, the converted PST needs to be the **primary/default** store in an Outlook
profile — which usually means a **new, dedicated Outlook profile** rather than adding the PST to
your everyday mail profile (where it would only ever be secondary).

This is **classic (desktop) Outlook only**. New Outlook and Outlook on the web cannot open local
`.pst` files at all, so neither can show this.

Steps (Windows, classic Outlook):

1. Close Outlook if it's running.
2. Open **Control Panel → Mail (32-bit)** (search "Control Panel" from the Start menu, then set
   *View by: Small icons*; on some setups this is under **Control Panel → User Accounts → Mail**).
3. Click **Show Profiles…**.
4. Click **Add…**, give the new profile a name (e.g. "ContinuMail viewing"), and click **OK**.
5. When prompted to add an account, skip/cancel account setup — you just want an empty profile —
   then, still in the profile's settings, add the converted `.pst` as a data file (or use
   **Control Panel → Mail → Show Profiles → (select the new profile) → Properties → Data Files →
   Add…** and browse to the `.pst`).
6. Make sure that `.pst` is set as the profile's **default delivery/data store** (in the Data
   Files tab, select it and click **Set as Default**).
7. Back in **Show Profiles**, either set this new profile as "Always use this profile", or choose
   **"Prompt for a profile to be used"** so you can pick it when Outlook starts.
8. Start Outlook and select the new profile. The converted mailbox is now the default store, and
   categorized items should show their colours.

## Note

Uploading the converted PST's contents into an Exchange/Microsoft 365 mailbox and viewing them in
Outlook on the web (or new Outlook) is **untested** — whether the category colours carry over that
way is currently unknown (TBD). This guide covers the verified case: local classic Outlook with the
PST as the primary/default store.
