# Application user guide

[Documentation home](../README.md) | [PPTX templates](PPTX-TEMPLATES.md) | [API integrations](API-INTEGRATIONS.md)

The [template parameter reference](TEMPLATE-PARAMETERS.md) lists every supported `{{...}}` token by template type.

This guide covers the implemented organiser UI and speaker portal. Complete [local setup](LOCAL-SETUP.md) or [Azure setup](AZURE-SETUP.md) first.

## First-event walkthrough

1. Sign in, open **Groups**, and create/edit a group. Set its name, location, Meetup slug, organiser email, and social defaults; choose **Save Group**.
2. Open **Settings > General**, set the default organiser email, review the description template and bio preference, and choose **Save All Changes**.
3. Open **Events > Create Event > Quick Invite**. Select the group, date/time, venue, format, and one or two speakers. Add names/emails if known.
4. Choose **Create event & speaker links**. A draft event and speaker slots are saved. Copy each link, or use the email control. A valid supplied email triggers an automatic send attempt when the link appears.
5. Each speaker opens their own link, supplies their talk/profile/headshot, and chooses **Save My Details**. No organiser account is needed.
6. Open the event, review both sessions, date/time, location, registration URL, short URL, Zoom information, and status, then choose **Save Event**.
7. Open **Social & Slides**, inspect/download the PNG and PPTX, and check the deck in PowerPoint.
8. Create the Meetup event through its API or copy the title/description manually. Save the resulting real Meetup URL back on the event. Publish social posts deliberately, one platform at a time.

For your own visual branding, follow [creating a PPTX template](PPTX-TEMPLATES.md#creating-a-template-in-powerpoint).

## Navigation and access

| Screen/route | Purpose |
| --- | --- |
| Home `/` | Public landing page and sign-in |
| Dashboard `/dashboard` | Counts, upcoming meetups, quick actions, attendance |
| Groups `/groups` | Group settings and branding |
| Events `/events` | Search, filter, create, import, clone, delete |
| Speakers `/speakers` | Reusable speaker directory and Sessionize import |
| Settings `/settings` | General defaults and API credentials |
| Event editor `/events/edit/{id}` | Event details and **Social & Slides** tab |
| Quick Invite `/events/invite` | Minimal event creation and speaker invitation links |
| Speaker portal `/speaker/submit/{token}` | Single-use self-service submission |
| Date-choice portal `/speaker/choose/{token}` | Personal invitation or verified public submission with a date picker |
| Public CFP `/cfp/{key}` | Group speaking opportunities and email verification |

The older `/assets?eventId=<id>` address redirects to the event's Social & Slides tab. With no event ID it returns to Events.

Organiser screens require authentication, but current access is shared across the app: there are no per-group memberships, separate read-only organiser roles, or individual ownership boundaries. In local fallback mode there is one predefined developer identity, not a password-based user directory.

## Dashboard

The dashboard shows total events, upcoming scheduled events, speaker count, group count, and a short upcoming-event list. Use **View All Events** for the full history and **Create Event** for New Event or Quick Invite.

Attendance controls retrieve Meetup counts for events with a valid Meetup URL. A dash means unknown/not available, not zero. These values are snapshots, not a live attendee registration system; use refresh to request a new count. Public-site/API access restrictions can make counts unavailable.

## Groups

Open **Groups**, choose a group's edit action or create a new group. Changes across tabs are kept with **Save Group**; navigating away without saving can lose edits.

### Group Details

- **Group Name** is required.
- **Website URL**, **Description / Tagline**, **Default Location**, and **Default Hashtags** describe the group and feed content defaults.
- **Primary/Secondary Brand Color** and **Group Logo** are stored branding values. They do not automatically recolour an uploaded PowerPoint deck or replace its artwork.
- **Organiser Email** receives submission notifications for this group; blank falls back to Settings.
- Logos accept PNG, JPG/JPEG, or WebP, up to **2 MB**. Upload/remove, then save the group.

New-group and seeded defaults contain .NET Notts/CGI examples; replace them with your actual group details.

### Meetup tab

Set the **Meetup Group Slug (urlname)** to the path segment from `https://www.meetup.com/<slug>/`, not the full URL.

An optional **Meetup API Key Override** holds a legacy token. A connected global OAuth token can still take priority; it is not a separate per-group OAuth connection. See [configuration precedence](CONFIGURATION.md#sources-and-precedence).

Choose **Speaker Bios** to inherit the global setting or select third-person/original bios. Enter a **Meetup Description Template** to override the global template, or leave it blank to inherit. The built-in default includes a specific Nottingham venue paragraph, so customise it for other locations.

### Social tab

**Short URL Stem (Bitly)** is a naming convention. A stem such as `https://bit.ly/mygroup` produces a suggestion such as `https://bit.ly/mygroupoctober2026`.

**The app does not create or verify a Bitly redirect.** Create the short link yourself and check its target. Use **Use default** on the event to apply the suggestion, or enter a different URL.

**Social Post Template** is shared by X, BlueSky, and LinkedIn for this group. Platform-specific handles are substituted when possible; LinkedIn uses speaker/sponsor names. Blank uses the built-in template. See [social text placeholders](#social-text-placeholders).

### Sponsors tab

Choose **Add Sponsor** and enter its name, website, optional logo, X handle, BlueSky handle, and LinkedIn URL. Logos have the same format/2 MB limit as group logos.

**Copy** duplicates a sponsor record in this group. **Delete** asks for confirmation; deletion is persisted when you save the group.

Use `{{Sponsors}}` or `{{SponsorNames}}` in social text to include sponsors. Sponsor database records and logo uploads do **not** automatically replace logos/banners in PPTX decks or the built-in PNG design. Author the required sponsor artwork in the custom deck.

### Custom PPTX Template tab

Upload a **`.pptx`**, up to **100 MB**, then choose **Preview 1 speaker** and **Preview 2 speakers**. Token-filled slides display sample data; slides without placeholders are dimmed/unchanged. Previews approximate the finished PowerPoint, not an Office rendering.

Choose **Save Group** after uploading. Template upload alone does not persist the group's new template reference.

See [PPTX template authoring](PPTX-TEMPLATES.md) for the complete contract. A group can use one uploaded deck containing both speaker-count variants and optional social/next-event slides.

### Deleting groups

A group with events cannot be deleted. Review/remove or move those events first if deletion is genuinely intended. Group deletion is not a storage cleanup tool.

## Settings

**General** contains the global speaker bio preference, default organiser recipient, and Meetup description template. **API Integrations** contains BlueSky, Zoom, Meetup, Sessionize, X, and LinkedIn panels.

Use **Save All Changes** before leaving the page, connecting Meetup, or publishing. Filled-credential badges indicate presence, not successful live API authentication.

Saved non-blank settings override configuration. Clearing a field may expose an environment/user-secret value rather than disabling the feature. Credentials saved here are held in SQL; see [configuration and secrets](CONFIGURATION.md).

## Speakers

The directory stores reusable profiles across all groups. Search/filter the directory, then use **Add Speaker** or edit an existing profile.

### Details and headshots

Enter the full name, optional email/pronouns, original bio, and social profiles. Supported fields include LinkedIn, BlueSky, X, Facebook, GitHub/repository, and website.

The editable **Third-person bio** is generated from the original bio when appropriate; **Regenerate** replaces it with another generated conversion. This is deterministic text conversion, not a cloud AI rewrite. Review the result, especially names/pronouns, before publishing.

Speaker headshots accept PNG/JPEG/WebP and allow up to **10 MB**. Save the profile after uploading. Usable imported remote photos are copied into configured blob storage when saved. Deck/card generation uses initials placeholders when a photo cannot be loaded.

Headshots are centre-cropped for the deck; use a clear, centred face with space around it. Missing optional contact/social data is allowed.

### Sessionize profile lookup

**Find on Sessionize** accepts a speaker name or public Sessionize profile URL:

1. Search and select the correct profile if several match.
2. Compare current and incoming bio, photo, and links.
3. Select only the fields you want to apply, set pronouns separately, and choose **Apply selected**.
4. Review and save the speaker.

Public profile search is separate from the Sessionize event API. If name search is ambiguous or fails, paste the exact public profile URL.

### Importing from a Sessionize event

Use the directory's Sessionize import control with an API ID or public Speakers endpoint. Bulk import matches existing profiles by name, adds missing speakers, and fills blank fields on existing speakers rather than overwriting populated details.

On **Add Speaker**, **Import from Sessionize > Load Speakers** lets you choose one returned speaker to populate the form. Review and save it.

### Talks and deletion

Existing speaker profiles have a **Talks** tab showing bookings in this app and available public Sessionize talks. Event links open their editor.

A speaker linked to an event session cannot be deleted. Changing a directory profile affects future content generation for every event that references that speaker; decks already downloaded and posts already published do not update automatically.

## Events

Use **Events** to search by title/speaker and filter by group/status. **Upcoming / Live Meetups** is the default and contains non-draft events that have not yet ended, including meetings currently in progress. **Past** contains non-draft events whose scheduled end time has passed. **Drafts** shows drafts regardless of date. Switching views resets status filtering and paging. All three views show twelve events per page.

The create menu offers **New Event**, **Plan Upcoming Dates**, **Quick Invite**, and **Sync Past Meetup Events**.

### Upcoming dates and speaker bookings

Choose **Plan Upcoming Dates** and select a group. Use the Monday–Sunday month calendar to select multiple dates; move between months using the arrows or choose a month and year directly. Selected dates remain in the review list across months and years. Existing meetups are shown as links, and their days cannot be selected again. Past days cannot be selected.

Review the list on the right, remove any unwanted dates, then choose **Save dates**. All selected dates are created together as drafts **open for speaker bookings**, without speakers or talks. Drafts use 18:30 UK time, 90 minutes, Hybrid format and the group's default location; edit these details in each draft later. Changing groups clears the selection. A conflicting existing date blocks the whole batch, leaving your selection available to review.

Use **Open this draft date for speaker bookings** in an individual event's editor to open or close it. Existing drafts remain unchanged by the calendar workflow. Only future draft dates with fewer than two assigned speakers appear to invited and public CFP speakers.

Any assigned speaker reserves a place, even when their talk is blank or a fixed-date invite is awaiting completion. Two assigned speakers fill the date. One assigned speaker makes it a shared event; applicants see that they will share the event, not the other speaker's name or talk.

Availability is checked again when a submission is saved. If another person fills the date first, the speaker can choose another date without re-entering their profile or talk. Merely opening a link or verifying an email does not reserve a place.

Bookings leave events in Draft. Publishing remains an explicit organiser action. Closing bookings or changing an event to a non-draft status removes it from new date choices without cancelling existing assignments.

Changing a booked date requires acknowledgement in the event editor. Notify the assigned speakers and update calendars separately; rescheduling does not automatically send revised invitations.

### Public call for speakers

Open an existing group's **Call for Speakers** tab, enable public bookings and choose **Save CFP settings**. These controls save separately from **Save Group**. Copy and share the public link.

The public CFP page shows the group's logo, description, default location and website, plus up to three recent finished Published/Completed meetups as read-only cards. Where available, cards link to the event on Meetup.com in a new tab; no organiser editing controls or private speaker details are exposed. After a verification email is successfully sent, the page replaces its content with **Check your email for a verification link.** Delivery failures leave the form visible for retrying.

Applicants see only that group's open dates and can select a date by clicking its card. They enter their email and receive a personal link that expires after 30 minutes. Opening the link automatically verifies the email and opens the speaker profile and talk form. Their preferred date appears as a summary, not another date picker; date choices appear only if no date was supplied or it is no longer available. There is no additional verification button unless verification fails and needs retrying. Opening the link does not book a place or create a speaker. A successful submission confirms the place immediately; there is no approval queue.

After email verification, exactly one catalogue speaker with that email (matched ignoring case and surrounding spaces) is prefilled for editing. Submitting updates that existing profile. No catalogue details are disclosed before verification. If no unique match exists, the speaker can enter details or import a public Sessionize profile; submission creates a separate profile rather than choosing between duplicate catalogue records. Sessionize import preserves the verified email. Email verification alone does not create or update a speaker record or reserve a date. If email cannot be sent, the applicant sees an error and no booking is created.

For an existing speaker, the form looks for public Sessionize talks. Importing a Sessionize profile uses that exact profile's talks. Choose a talk to prefill its title and abstract, then edit them if needed, or select **Create a new talk** to start with blank talk fields. If Sessionize is unavailable or no public talks are listed, you can still enter a new talk manually.

Recognised speakers can also choose from their own previous talks recorded in this app, including talks at the same meetup group or other groups. The list shows the group and date, newest first, for finished Published/Completed meetups. Choosing a previous talk copies its title and abstract into the editable fields; it does not change the original talk. Public applicants only see this history after email verification and a unique catalogue match.

The private submission page uses the invited meetup group's name, logo, description, website and brand-colour accents, so speakers can identify the community they are submitting to. Branding remains visible with the booking confirmation.

Disable the CFP or choose **Revoke old link and create a new one** to invalidate the old public link and outstanding verification links. Existing confirmed bookings and personalised invitations are unaffected. Requests are rate-limited; repeated requests may require waiting before retrying.

### New Event and editing

Select a group, enter a title/agenda, and set date/time, duration, format, venue, location/platform, and optional promo subtitle.

Date fields in the event editor and Quick Invite open a month calendar with Monday-Sunday headings, month/year navigation and the selected weekday shown in the field. Set the UK start time separately below the date.

Organiser calendars and speaker CFP date choices mark **England-and-Wales public holidays** with different shading and a circular **PH** badge. The holiday name appears on hover and is available to screen readers; selected-date summaries and CFP cards also show the name. Holidays remain selectable and do not change booking availability.

Holiday information comes from the official GOV.UK bank-holiday feed, including published substitute days and exceptional holidays, and is cached for up to a day. If the feed cannot be loaded or does not cover the selected year, an explicit warning asks you to check holidays separately. Dates remain usable; missing data does not mean there are no holidays. Scotland, Northern Ireland and school holidays are not included.

The editor supports one or two speaker slots. Select an existing speaker, add one through the directory, or generate a magic link for a placeholder speaker. Choose a Sessionize talk through the talk picker, or **Create a new talk** and enter title/abstract manually. Existing populated slots offer **Change speaker or talk**.

Picking a talk can regenerate the event title/description. Review the text afterwards; the event agenda should not accidentally contain duplicate generated speaker sections.

**Save Event** persists the event and slots. Saving a new event opens its editor with the **Social & Slides** tab available. Selecting one speaker on an existing two-speaker event and saving removes the second event session.

### Dates, formats, and status

The event editor and Quick Invite date inputs use **UK time** (`Europe/London`) independently of the web server's timezone. Stored values are UTC. Date choices display GMT/BST explicitly; ambiguous or nonexistent clock-change times cannot be saved. The fixed-date speaker portal's existing context displays UTC.

Check the scheduled start in Zoom after API creation; changes to the local schedule do not update external meetings automatically.

| Field | Behaviour |
| --- | --- |
| Hybrid | Promo venue line describes venue plus Zoom |
| In Person | Promo venue line describes venue only |
| Online | Venue field disabled; promo venue line uses Zoom |
| Promo Subtitle | Optional extra header line; no subtitle when blank |
| Draft / Scheduled / Published / Completed | Stored workflow labels, not timers that trigger external actions |

When no venue name is supplied, promotional formatting can fall back to **CGI**. Set the real venue and review the header preview.

### Zoom

Choose **Generate Zoom Link** to create a scheduled meeting when API credentials are configured, or to apply the fixed default meeting URL/passcode from Settings. You can also enter a join URL/passcode manually.

Save the event afterwards. Repeated API generation may create additional external meetings; changing the local event later does not automatically update/cancel the external meeting.

### Pull Sessions from Sessionize

Enter a Sessionize **API event ID** and choose **Pull Sessions**. The current editor takes the first two returned sessions and the first speaker for each, creating missing directory speakers as needed. It is not a multi-track programme synchroniser or a session-selection wizard.

Use an event ID when pulling both speakers and sessions: a single full endpoint URL is used unchanged by each client method and cannot represent both response shapes. Review both slots and save the event afterwards.

### Quick Invite and magic links

Quick Invite offers **New Speaker**, **Import from Sessionize**, or **Use Existing Speaker** for each slot. Existing speakers are reused, with their profile prefilled for review/edit.

With a fixed date, Quick Invite creates a draft event and its assigned sessions. Alternatively choose **Let each speaker choose an upcoming date** to create independent group invitations without creating an event or reserving a place. Each invited speaker can choose a different open draft date.

The **New Event** form also offers **Let the speaker choose an upcoming date**, which opens the same invitation controls instead of creating an undated event. Existing dated events keep fixed-date invitations.

You can also choose **Magic Link** on a normal event slot. This saves the event/session first and creates a placeholder profile if none is selected.

- Links expire after **14 days** by default and work for one successful submission.
- Generating a new link revokes previous unused links for that session.
- Only the SHA-256 token hash is stored; treat the full URL as a credential.
- Link controls show **Copy** and **Email link**. A valid known email triggers an automatic send attempt when a new link is displayed; generating/regenerating a link can send real email.
- If email is not configured, nothing is sent; copy the URL manually.
- Links use the hostname from which the organiser generated them. `localhost` links are not reachable by remote speakers.

There is no user-facing list for recovering raw links after leaving the page. Generate a replacement if necessary and send only the newest one.

### Speaker self-service portal

The invited speaker opens their unique link without signing in, enters the talk title/abstract, full name, email, optional pronouns/bio, headshot, and social profiles, then chooses **Save My Details**.

Success saves the profile/session and consumes the token. The app may replace a placeholder event title with the submitted talk/speaker title. It attempts an organiser notification and, for a valid speaker email, a confirmation with calendar attachment and optional inline RSVP request. Email failure does not undo the saved submission.

After submission, the speaker must ask for another link to make changes, or the organiser can edit the profile/session. The app does not track replies to calendar invitations.

### Cloning and deleting

**Clone** creates a draft dated one month after the source and copies title (with `(Copy)`), description, duration, location, group, and sessions referencing the same directory speakers.

Do not assume all other fields are cloned: review format, venue, subtitle, registration/short URLs, and Zoom details. Magic links, external event identity, attendance, and published social posts are not carried over as new external resources.

**Delete** asks for confirmation and removes the local event, sessions, associated tokens, and generated-asset records. It does not delete external Meetup events, Zoom meetings, social posts, or uploaded blobs.

### Sync Past Meetup Events

1. Filter Events to a **specific group**.
2. Choose **Create Event > Sync Past Meetup Events**.
3. Review the returned history, title/details, and inferred speakers.
4. Match each person to an existing directory profile or mark **Not the same person**. Optionally apply selected Sessionize fields, or use Meetup details.
5. Choose **Import events**.

Existing external Meetup event IDs are skipped. Imported events are marked **Completed**; speakers may be reused, enriched, or created. Review inferred speaker/talk data because external descriptions are not a guaranteed structured source.

OAuth/API access is preferred. The service can attempt a public Meetup-page fallback when credentials/access are unavailable, but public-page extraction can be incomplete or blocked. This is a one-way import, not continuous synchronisation.

## Social & Slides

Save the event before opening this tab. It prepares the event's social image, Meetup title/description, and separate LinkedIn/X/BlueSky text.

### Downloads and image refresh

Choose **Download Social PNG** or **Download PPTX Deck**. Files are generated from current stored data on request, rather than retrieved from a saved asset library. **Refresh** reloads the stored event and regenerates the image; it does not save edits or regenerate all text fields.

The built-in image renderer uses 1280x720. A custom deck's social slide is rendered at width 1280 using the deck's aspect ratio, so author it at **16:9** to produce 1280x720.

Save changes to speakers/groups/events before reloading the assets tab. Reopen/reload it to regenerate text after source/template changes.

### Meetup publication

The editable title/description fields support **Copy Title** and **Copy Description** for pasting into Meetup's create-event form.

**Important:** **Publish to Meetup** currently uses the saved event and regenerates its description from the configured template. It does **not** submit edits made only in the copyable preview fields.

Without API credentials, the app explicitly reports that **no event has been published** and offers a link to the configured Meetup group. Open that group and create the event manually, using the copyable title/description and downloaded social PNG. A valid group URL name must be configured; the app will not substitute an unrelated group.

With working credentials, the API creates a new external event. The current tab does not automatically write the returned URL/ID or Published status back to the local event and is not an update-in-place publisher. Copy the real event URL into the event editor and save. Avoid clicking Publish again without checking Meetup, as this can create duplicates.

### Meetup website extension (Chrome and Edge)

#### Formatting descriptions in the app

The **New/Edit Meetup > Event Description / Agenda**, global **Settings > Meetup Description Template**, group-specific template and **Social & Slides > Meetup.com Description** use a rich-text editor: formatting appears as you edit rather than as Markdown markers. Save agenda changes with **Save Event**. Select text (or place the cursor on a line for headings/lists), then use the toolbar. **Ctrl+B** toggles bold, **Ctrl+I** italic, **Ctrl+K** inserts a link, and **Ctrl+Shift+7/8** creates numbered/bullet lists (use Cmd instead of Ctrl on macOS). Link prompts accept HTTP(S) addresses only. Undo/Redo are available on the toolbar and through normal browser editing shortcuts. Pasted content is inserted as plain text; apply formatting in the editor afterward. Template placeholders remain literal until an event is generated. Existing Markdown templates load formatted; content is still stored as Markdown, without a database migration.

Save global/group templates using their existing Save action. Publication preview edits are temporary and apply to **Copy Description** and **Prepare for Meetup**; they do not change the saved event, direct API publication or extension API loading. Copy Description includes formatted HTML and Markdown text when the browser supports rich clipboard access, otherwise reports its plain-text fallback. The extension converts basic Markdown formatting into Meetup rich text; reload the extension after updating it to enable italic and list conversion. Review before saving or publishing on Meetup.

The **Meetup Organiser Website Handoff** extension is an assisted alternative to API publication. It fills supported controls only; it does not save, publish, announce, collect attendees or bypass login/challenges. Confirm that your use complies with Meetup's terms before using it.

#### Load saved events directly through the authenticated API

Reload the extension on `chrome://extensions` or `edge://extensions` after updating it. Version 0.3 adds a compact connection/event picker, persistent 30-day sign-in and automatic connection completion. Restart the updated app so its normal startup migration creates the extension grant table. The extension requests optional permission for your app's host; no broad site access is granted automatically.

1. Open the extension, enter your app's exact HTTPS origin (or HTTP localhost including its port), and choose **Connect to Meetup Organiser**. Accept the browser's permission request for that app host only.
2. The app opens in a normal browser tab. Sign in with your existing organiser account and choose **Approve read-only access** only if you just started this request yourself. The request expires after five minutes. No password or login cookie is sent to the extension.
3. Reopen the extension. It collects the approval automatically; there is no mandatory second connection button. If approval is still pending, finish approving in the app and reopen the popup, or use **Check approval**. Approval grants read-only access for 30 days, including across browser/app restarts. The connection card displays the app address and expiry date. Existing version 0.2 connections retain their original short expiry; reconnect to obtain 30 days.
4. On the correct Meetup create/edit tab, search by event title or meetup group, select an upcoming event and click **Fill Meetup Fields**. The extension loads the event and immediately replaces supported fields, including start time, without a preview or confirmation checkboxes. Use **Previous** and **Next** for further results. The app supplies saved content, not unsaved Social & Slides edits; use the capture workflow below for those.
5. **Download PNG** remains available after loading, including if filling fails. Check all results and manual steps, especially the date and Europe/London time zone. Save/publish on Meetup yourself.

**Sign out / switch app** revokes that grant and clears the local connection, pending approval and prepared content. You can then enter another app address, such as the live system instead of localhost, and connect normally. If the old app is offline, local sign-out still works, but a warning explains that remote revocation is unconfirmed and links to the old app's revocation page. This disconnects the extension, not your normal app or Microsoft browser sign-in. To revoke all your grants, open `/extension/connect` in the app and choose **Revoke my extension access**. Expired or revoked connections require approval again; there are no refresh tokens. API permissions are read-only, so recording a saved/published result still happens explicitly inside the app.

The server stores only SHA-256 grant hashes in the shared SQL database; approved connections survive app restarts. Five-minute pending pairing requests remain in process memory: restarting during approval requires a new connection, and multi-instance deployments need instance affinity for pairing (or a shared pairing store). Pairing uses a SHA-256 proof challenge bound to the extension's random verifier, is single-use and requires authenticated organiser approval. Pairing/API requests are rate-limited and their responses are not cacheable. Access follows the existing organiser policy: any signed-in organiser can currently access all events; there is no per-group membership model.

#### Prepare and capture content without API permissions

1. Open `chrome://extensions` or `edge://extensions`, enable **Developer mode**, choose **Load unpacked**, and select the repository folder `extensions\meetup-website`. No build is required. Pin the extension if useful.
2. Open the extension and enter your app's exact HTTPS origin, such as `https://your-app.azurewebsites.net`, expand **Use prepared content instead**, then choose **Trust app address for capture**. HTTP localhost is allowed for development. Do not include a path. The extension uses temporary access to the tab you invoke it on, not all-site permissions. Sign out first if switching away from an existing API connection.
3. Set the group's **Meetup URL name** in this app. Save the event's date/time, duration, format and location. In **Social & Slides**, edit the copyable title/description as needed and wait for the image preview. Choose **Prepare new event for Meetup** or **Prepare update for Meetup**. Unlike API publication, this handoff uses the edited preview text.
4. On that app tab, open the extension, expand **Use prepared content instead** and choose **Capture content from this tab**. The event content expires after 30 minutes, independently of the 30-day sign-in. A linked Meetup event means an update to that exact event; it will not silently create a replacement.
5. Open the target group/event on Meetup, log in manually, and navigate to its create/edit form. Open the extension again and choose **Fill Meetup fields**. The click immediately replaces supported fields, including start time; there are no preview or confirmation checkboxes. Check the date and Europe/London time zone manually. Target group and linked event identity are still enforced.
6. Complete every manual step shown by the extension. The evidenced edit form supports title, description and online meeting link. The start date, duration/end and venue/format remain manual. The active Markdown editor receives Markdown. In rich-text mode, Markdown headings become bold paragraphs without the leading `#`; bold, italic, bullet/numbered lists and HTTP(S) Markdown links are converted. Standalone labels such as speaker names and "How to find us:" are bold; contact labels before HTTP(S) URLs are bold too. Paragraph/blank-line spacing is retained; review the result before saving. Advanced Markdown is not converted. For the image, open Meetup's image edit menu, choose **Replace**, and leave the **Choose photo** dialog open. Open the extension, reconfirm the target and choose **Send PNG to open photo chooser**. It supplies the generated PNG to the evidenced file input without accessing your filesystem. Review upload errors and the crop/resize dialog and apply the image yourself. Alternatively download the PNG and use **Choose photo** manually. Never choose Remove merely to replace the image.
7. Review all values, then click **Save changes**, save a draft or publish **on Meetup yourself**. Back in this app, expand **I have saved or published this event on Meetup**, enter the real event URL and outcome, tick the explicit confirmation and choose **Record my confirmation**. This persists the URL/ID and local Draft/Published status as your report, not as independently verified publication. Published events disappear from new CFP availability.

The supplied edit-page and new-event structures are covered by synthetic form/bridge tests and native offline Chrome/Edge checks for text editing and PNG file-input assignment. The new-event form at `/<group>/schedule/` is supported, along with `/<group>/events/create/`; updates still require the exact linked event's edit route. The evidenced title, Markdown description and confirmed start-time controls can be filled on the schedule page. Fill errors appear beside the action buttons. Live Meetup framework persistence and upload behaviour remain unverified. The extension refuses unsupported routes/forms rather than guessing. A create URL is not constructed or opened automatically: open the group's actual create form yourself. If its route/controls are unsupported, use the existing copy fields and PNG download, then record the result manually.

On the supplied new-event image panel, **Select** opens the system file chooser directly and no associated file input was included in the snapshot. Use **Download event PNG** and select that downloaded file manually. The extension does not attempt to control the operating system's file dialog.

The extension keeps the trusted app origin and 30-day API credential in browser-local extension storage, restricted to trusted extension contexts and not synced to other browsers. Pairing proofs and prepared public event content (including its meeting link) stay in extension session storage. Prepared content is cleared, replaced, or discarded when found expired on use; session data is lost when the browser session ends. Choose **Clear** after completing a handoff or **Sign out / switch app** to revoke and clear the connection. It never reads cookies or passwords, sends telemetry, or receives speaker verification tokens. Do not commit raw Meetup HTML snapshots; they can contain private account/member data. Website changes can require extension updates; use the manual fallback if a control is missing, ambiguous or fails verification.

For extension development, run `npm ci --prefix .\extensions\meetup-website` and `npm test --prefix .\extensions\meetup-website`. Tests use synthetic, sanitised controls, not captured account HTML.

### LinkedIn, X, and BlueSky

Review/edit each platform's text. Leave **Attach social card image** checked to include the prepared PNG through the API, or uncheck it for text/link-only posting.

API buttons use the corresponding editable text. They post immediately and independently; there is no scheduled publishing queue, automatic retry history, or cross-platform transaction.

For manual posting, use **Web Share** / **Web Intent**, or copy the text into the platform and attach the downloaded PNG yourself. Web fallbacks do not upload images. The X/LinkedIn fallback URLs are prepared from the original generated text, not live textarea edits; copy your revised text manually. BlueSky's intent follows its edited text.

Watch the displayed X (280) and BlueSky (300) character counts, but let the platform/API response be authoritative for its actual rules. LinkedIn API posting is to a personal author, not an organisation. See [API integrations](API-INTEGRATIONS.md) for permissions, tokens, and error handling.

## Text template reference

These text systems are independent of PPTX replacement. A placeholder accepted by one is not automatically accepted by another.

### Meetup description placeholders

Use `{{Name}}` in **Settings > General > Meetup Description Template** or the group's Meetup override:

| Placeholder | Value |
| --- | --- |
| `{{EventTitle}}` | Event title |
| `{{EventDate}}`, `{{EventTime}}` | Formatted event date and UK start time |
| `{{EventDescription}}` | Event's stored description/agenda |
| `{{TalkTitle}}` | First talk title |
| `{{SpeakerName}}` | Speaker name(s) |
| `{{Talks}}` | Talk titles, speakers, and third-person descriptions |
| `{{TalkDescriptions}}` | Talk descriptions only |
| `{{SpeakerSection}}` | Speaker heading, selected bios, contact links |
| `{{Location}}` | Location/platform |
| `{{ZoomUrl}}`, `{{ZoomPasscode}}` | Saved Zoom details |
| `{{ZoomSection}}` | Combined Zoom block, blank without a link |
| `{{GroupName}}`, `{{WebsiteUrl}}`, `{{Hashtags}}` | Group values |

Placeholders are case-insensitive. Known empty values become blank; unknown names remain visible, so fix typos. Third-person/original preference primarily controls published speaker bios; review any automatically converted talk text too.

A location-neutral starting template:

```text
# {{EventTitle}}

{{EventDescription}}

{{Talks}}

{{SpeakerSection}}

When: {{EventDate}} at {{EventTime}} UK time
Where: {{Location}}

{{ZoomSection}}

{{Hashtags}}
```

Keep the event's stored agenda concise when your template adds talks/bios, to avoid duplication.

### Social text placeholders

Use these in the group's **Social > Social Post Template**:

| Placeholder | Value |
| --- | --- |
| `{{GroupName}}`, `{{EventTitle}}` | Group/event name |
| `{{SpeakerNames}}` | Names joined with "and" |
| `{{SpeakerMentions}}` | X/BlueSky handles with name fallback; LinkedIn names |
| `{{TalkTitles}}` | Quoted talk titles joined with "and" |
| `{{ShortDate}}`, `{{ShortTime}}` | Short UK date and 24-hour start time |
| `{{Where}}` | Venue/Zoom summary based on format |
| `{{Venue}}`, `{{Format}}` | Venue and format label |
| `{{ShortUrl}}` | Short URL, falling back to Meetup URL, group website, then a default |
| `{{Hashtags}}` | Group defaults |
| `{{Sponsors}}`, `{{SponsorNames}}` | Platform handles/name fallbacks, or names |

When there are no named sponsors, lines containing sponsor placeholders are dropped so a trailing "Thanks to" is not left behind.

Example:

```text
Next {{GroupName}}: {{SpeakerMentions}} on {{TalkTitles}}

{{ShortDate}} at {{ShortTime}} UK time
{{Where}}
Register: {{ShortUrl}}

{{Hashtags}}
Thanks to {{Sponsors}}
```

Shorten the template for platform limits. Handle substitution is not a guarantee of a platform-recognised rich mention.

## Before a real announcement

Review the saved date/time, format, actual registration/short-link target, both speakers' names/talks/photos, sponsor branding, and the finished PNG/PPTX. Confirm the intended external account and that API buttons will make real writes. Changes after publication require manual follow-up on the destination platform.
