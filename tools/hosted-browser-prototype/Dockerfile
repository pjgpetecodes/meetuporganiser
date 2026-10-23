FROM mcr.microsoft.com/playwright:v1.64.1-noble

WORKDIR /app/tools/hosted-browser-prototype
COPY tools/hosted-browser-prototype/package.json tools/hosted-browser-prototype/package-lock.json ./
RUN npm ci --omit=dev --no-audit --no-fund

COPY tools/hosted-browser-prototype/ ./
COPY extensions/meetup-website/form.js /app/extensions/meetup-website/form.js
COPY extensions/meetup-website/handoff.js /app/extensions/meetup-website/handoff.js

ENV BROWSER_CHANNEL=chromium
USER pwuser

CMD ["xvfb-run", "-a", "npm", "test"]
