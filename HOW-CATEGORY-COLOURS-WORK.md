# How baked-in category colours work

ContinuMail bakes your Outlook **category colours** directly into every converted PST, so they travel with
the file — no Outlook automation, no COM, no add-in, no separate "apply" step.

## The mechanism

Outlook stores its **master category list** — the mapping of category *name* → *colour* — as a hidden
configuration message (`IPM.Configuration.CategoryList`) in a store's Calendar folder. The list itself is a
small XML document (`PidTagRoamingXmlStream`, per MS-OXOCFG).

At convert time, ContinuMail builds that list from your sources — Thunderbird mail **tags** plus **calendar
and task categories** (each with the colour Thunderbird shows: your profile's colour override if set,
otherwise Thunderbird's own computed default) — and writes it, as that exact hidden configuration message,
into a top-level `Calendar` folder of the converted PST. Every categorized item already carries its category
*names*; this baked list is what turns those names into *colours*.

## Where colours appear (and where they don't)

Classic Outlook reads the master category list from your **primary / default store only**, then caches it.
So:

- **Open the converted PST as your primary/default store** → the baked list is used → categories render in
  colour. See **[Viewing category colours](VIEWING-CATEGORY-COLOURS.md)** for the exact steps.
- **Attach the converted PST as a *secondary* data file** → categorized items show their **names but not
  their colours** (Outlook ignores a secondary store's own list). This is an Outlook behaviour, not a
  conversion defect.

Because the list is read from whichever store is primary, **every** converted PST carries the **full union**
of all categories across the conversion — so whichever one you make primary, everything colours.

## Scope

- Classic Outlook on Windows only. New Outlook and Outlook on the web can't open local PST files.
- Whether uploading a converted PST into an Exchange / Microsoft 365 mailbox surfaces the colours in Outlook
  on the web is untested.
