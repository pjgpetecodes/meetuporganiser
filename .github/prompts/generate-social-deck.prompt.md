---
description: Prompt to trigger PPTX presentation deck generation and 1280x720 social card rendering.
---

Generate promotional presentation assets and social cards for the following meetup event:

- **Event Title**: {{title}}
- **Group**: {{group}}
- **Date & Time**: {{dateTime}}
- **Location/URL**: {{locationOrUrl}}
- **Speaker(s)**: {{speakers}}

Please provide:
1. Instructions to download the OpenXML `.pptx` slide deck from `/api/events/{id}/pptx`.
2. The 1280x720 social image card layout and preview.
3. Verification that all placeholders (date, speakers, talk titles, group logo) are cleanly populated.
