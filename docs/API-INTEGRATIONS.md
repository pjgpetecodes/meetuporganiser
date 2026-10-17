# API integrations setup

[Documentation home](../README.md) | [Local setup](LOCAL-SETUP.md) | [Azure setup](AZURE-SETUP.md) | [User guide](USER-GUIDE.md)

Configure integrations in the app at **Settings → API Integrations**. Save the settings before using **Connect Meetup** or trying to publish. The integration panels show whether API credentials are present and which fallback is available.

You can also provide settings through ASP.NET Core configuration. The matching environment-variable names use double underscores, for example `Integrations__BlueSky__Handle`. A non-empty value saved in the Settings page takes precedence over configuration. Settings entered in the app are stored in its SQL database; restrict access to Settings and prefer Azure Key Vault or environment variables for production secrets.

## BlueSky

BlueSky posting uses the AT Protocol directly. No developer API key or app registration is required.

1. In BlueSky, open **Settings → Privacy and security → App passwords** and create an app password.
2. In the app's BlueSky panel, enter your handle and app password, then save.
3. Open the saved event's **Social & Slides** tab, select **Attach social card image** if desired, and choose the BlueSky post button.

Enter a domain-verified handle as a domain, such as `petecodes.co.uk`—without an `@` prefix, spaces, or profile URL. Use the generated app password, not your BlueSky account password. The web-intent button remains available as a manual fallback; it cannot attach an image.

Configuration keys:

| Setting | Value |
| --- | --- |
| `Integrations:BlueSky:Handle` | BlueSky handle or verified domain |
| `Integrations:BlueSky:AppPassword` | BlueSky app password |

## X (Twitter)

The app publishes posts using OAuth 1.0a user-context credentials. The X developer account and app must have API access for creating posts and uploading media. Check X's current plan and endpoint availability before configuring credentials.

1. In the [X Developer Portal](https://developer.x.com/en/portal/dashboard), create or select a project and app.
2. Configure user authentication as **OAuth 1.0a**, with **Read and write** app permissions. Regenerate user access tokens after changing permissions.
3. Copy the app's API key and API secret, plus the user access token and access token secret, into the X panel in Settings.
4. Save the settings. In the event's social-posting page, check **Attach social card image** to upload the PNG with the post, or uncheck it to publish text and its included event link only.

All four values are required for API posting. The app uploads a selected PNG through X API v2 (`POST /2/media/upload`, `media_category=tweet_image`) and creates the post through `POST /2/tweets`. X image uploads are limited to 5 MB. The **Web Intent** button is kept as a manual fallback; X's compose intent does not attach the generated image.

Configuration keys:

| Setting | Value |
| --- | --- |
| `Integrations:Twitter:ApiKey` | X API key (consumer key) |
| `Integrations:Twitter:ApiSecret` | X API secret (consumer secret) |
| `Integrations:Twitter:AccessToken` | User access token |
| `Integrations:Twitter:AccessTokenSecret` | User access token secret |

See X's documentation for [developer apps](https://docs.x.com/resources/fundamentals/developer-apps), [OAuth 1.0a credentials](https://docs.x.com/resources/fundamentals/authentication/oauth-1-0a/api-key-and-secret), [creating posts](https://docs.x.com/x-api/posts/create-post), and [media uploads](https://docs.x.com/x-api/media/upload-media).

## LinkedIn

LinkedIn API posting targets a **personal profile**, not an organization.

1. Create/select a LinkedIn developer app and add the **Share on LinkedIn** product.
2. Generate a member authorization token with the `w_member_social` permission.
3. In the LinkedIn panel in Settings, enter the access token and your personal author URN (`urn:li:person:...`), then save. The client ID and client secret may be recorded there, but the current posting flow uses the supplied access token and author URN.
4. On the event's social-posting page, select whether to attach the generated social card and choose **Post via API**.

The token can expire or be revoked; replace it with a fresh token when posting fails. The web-share link remains available as a manual fallback.

Configuration keys:

| Setting | Value |
| --- | --- |
| `Integrations:LinkedIn:AccessToken` | Member access token with `w_member_social` |
| `Integrations:LinkedIn:AuthorUrn` | Personal member URN, beginning `urn:li:person:` |
| `Integrations:LinkedIn:ClientId` | Optional app client ID |
| `Integrations:LinkedIn:ClientSecret` | Optional app client secret |

## Meetup.com

Meetup OAuth enables event publishing and importing past events. Public-page import can be attempted when API access is unavailable, but may be incomplete or blocked.

1. Register an OAuth client with Meetup and set its redirect URL to the exact callback URL used by this deployment. For local development, the default is `http://localhost:5107/integrations/meetup/callback`; use `https://localhost:7225/integrations/meetup/callback` when browsing the HTTPS launch profile. In Azure use `https://<your-app-host>/integrations/meetup/callback`.
2. In the Meetup panel in Settings, enter the OAuth client ID, client secret, redirect URL, and default group URL name. Save.
3. Choose **Connect Meetup** and authorize the app in Meetup.
4. If a group uses a different URL name, set its slug in that group's settings; it overrides the default.

The app stores its OAuth access and refresh token set protected with ASP.NET Core Data Protection and refreshes access tokens when needed. A legacy manually supplied access token is available as an optional fallback; the OAuth connection is preferred.

Preserve the Data Protection keys when moving/restoring an environment, or reconnect Meetup. The current OAuth connection is global, not per group, and a usable OAuth token takes priority even over a group-specific legacy token override.

**Publication caveats:** the event's **Publish to Meetup** button uses the saved event/template, not edits made only in the copyable preview textboxes. Without a token it returns the group's manual `/events/create` URL, which does not mean an event was created despite the UI success label. With credentials it creates a new event, not an update of an existing one. Save the real returned event URL back on the local event manually and avoid repeat clicks that could create duplicates.

Configuration keys:

| Setting | Value |
| --- | --- |
| `Integrations:Meetup:ClientId` | Meetup OAuth client ID |
| `Integrations:Meetup:ClientSecret` | Meetup OAuth client secret |
| `Integrations:Meetup:RedirectUri` | Exact registered callback URL |
| `Integrations:Meetup:Urlname` | Default Meetup group URL name |
| `Integrations:Meetup:ApiKey` | Optional legacy access token |

## Zoom

To create scheduled meetings through Zoom, create a **Server-to-Server OAuth** app in the [Zoom App Marketplace](https://marketplace.zoom.us/). Grant the app the meeting-creation permission required by your Zoom account, activate it, and enter its Account ID, Client ID, and Client Secret in the Zoom panel in Settings.

If you do not have API access, enter a fixed default meeting URL and optional passcode instead. The app uses that link when API credentials are not configured; it does not create unique Zoom meetings in this mode.

If neither API credentials nor a fixed URL are configured, generation returns an error rather than inventing a meeting link. Save the event after generating/applying the link. Check the actual Zoom scheduled time: the current editor uses server-local date/time conversion, while the API request is formatted as UTC. Later local edits do not automatically update or cancel external meetings.

Configuration keys:

| Setting | Value |
| --- | --- |
| `Integrations:Zoom:AccountId` | Server-to-Server OAuth account ID |
| `Integrations:Zoom:ClientId` | Server-to-Server OAuth client ID |
| `Integrations:Zoom:ClientSecret` | Server-to-Server OAuth client secret |
| `Integrations:Zoom:DefaultMeetingUrl` | Optional fixed meeting URL when not using the API |
| `Integrations:Zoom:DefaultPasscode` | Optional passcode for that fixed meeting |

## Sessionize

Sessionize speaker and session data is read from its public REST view endpoint; no API key is needed.

1. In Sessionize, open the event's **API / Embed** settings.
2. Copy the event ID or a full **View Speakers / Sessions** endpoint URL into the Sessionize panel in Settings, then save.

When given an event ID, the app requests the public `view/Speakers` and `view/Sessions` endpoints. A supplied endpoint URL is used directly.

For **Event editor > Pull Sessions**, use an event ID rather than one full URL, because that action requests both response types. It fills the first two sessions and the first speaker from each, not an entire multi-track programme.

Public speaker-profile lookup is a separate flow: use **Find on Sessionize** in the speaker/event UI, search a name or paste a public profile URL, compare fields, and apply only the fields you want. It does not require the event API ID.

Configuration key:

| Setting | Value |
| --- | --- |
| `Integrations:Sessionize:DefaultEventId` | Sessionize event ID or full public view endpoint |

## Configuration example

For local development, values can be supplied through user secrets or another local configuration provider. In Azure, use Key Vault references or App Service application settings instead of committing credentials:

```json
{
  "Integrations": {
    "BlueSky": {
      "Handle": "your-handle.example",
      "AppPassword": "<app-password>"
    },
    "Twitter": {
      "ApiKey": "<api-key>",
      "ApiSecret": "<api-secret>",
      "AccessToken": "<user-access-token>",
      "AccessTokenSecret": "<user-access-token-secret>"
    },
    "LinkedIn": {
      "AccessToken": "<member-access-token>",
      "AuthorUrn": "urn:li:person:<member-id>"
    },
    "Meetup": {
      "ClientId": "<client-id>",
      "ClientSecret": "<client-secret>",
      "RedirectUri": "https://your-host/integrations/meetup/callback",
      "Urlname": "your-group"
    },
    "Zoom": {
      "AccountId": "<account-id>",
      "ClientId": "<client-id>",
      "ClientSecret": "<client-secret>"
    },
    "Sessionize": {
      "DefaultEventId": "<event-id>"
    }
  }
}
```

Meetup's connected OAuth token set is managed by the app and should not be entered manually in configuration.

## Using manual fallbacks

In the event's **Social & Slides** tab, use the copy buttons for Meetup title/description, or the adjacent social **Web Share / Web Intent** controls. Download the PNG and attach it manually; compose links do not upload images.

API post buttons use edited platform text, but X/LinkedIn fallback URLs are prepared from the original generated text. Copy revised text manually when using those fallbacks. BlueSky's intent reflects its current textarea. Changing a saved credential does not publish anything automatically.

Credentials being present is not a live validation of permissions, account access, API subscription, or token freshness. Third-party plans/approval processes change; verify requirements in each provider's current developer portal.

## Troubleshooting

- **Credential errors:** verify there are no extra spaces, that each credential belongs to the same app/account, and that the required permissions are enabled.
- **X or LinkedIn API post fails:** use the displayed error, check that API access/token permissions are active, and use the adjacent web fallback to compose manually.
- **BlueSky `InvalidEmail`:** enter the handle/domain without `@` and authenticate with an app password.
- **Image upload fails:** verify the image option is checked and the generated PNG meets the platform's file-size limit. Uncheck the image option to publish a link-only post.
- **Meetup authorization fails:** ensure the redirect URL in Meetup exactly matches the saved URL, including scheme, host, port, path, and trailing-slash behavior.
