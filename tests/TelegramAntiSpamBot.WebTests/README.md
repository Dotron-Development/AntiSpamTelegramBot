# Review Web Tests

Independent Playwright npm project for the React review app. Requires Node.js 24 LTS and a free `127.0.0.1:5179` port.

From the repository root:

```powershell
npm --prefix src/web ci
npm --prefix tests/TelegramAntiSpamBot.WebTests ci
npm --prefix tests/TelegramAntiSpamBot.WebTests exec -- playwright install chromium
npm --prefix tests/TelegramAntiSpamBot.WebTests test
```

Or run `npm ci`, `npx playwright install chromium`, and `npm test` from this directory after installing the web app dependencies.

The configuration starts Vite from `src/web`, explicitly bound to `127.0.0.1`, with test-only `API_URL` and `FUNCTION_KEY` values. It does not reuse the manually running dev server or depend on your local `.env`. Tests mock browser API requests; the CORS integration scenario starts a separate ephemeral loopback HTTP server and verifies direct browser GET/POST requests and OPTIONS preflight. No Azure or Telegram moderation calls are made.

Coverage includes env URL selection, backend chat selection and refresh, empty/error chat catalogs, chat and message pagination, root and `/api` URL handling, precise numeric IDs/ticks, safe text rendering, confirmations, independent restriction removal, errors without automatic retries, credential storage, and responsive desktop/mobile layouts. Form regressions cover invalid/overflowing IDs without requests, trimming without precision loss, fresh defaults and cleared errors on reopening, and submission locks that prevent cancellation or duplicate mutations. Pagination regressions also verify that changing the page-size select does not change the already submitted query or selection. All-chat scenarios cover button-only loading, absent chat filters, empty/failed catalogs, filter persistence, and selection/moderation of messages whose IDs coincide across different chats.

Screenshots and failure traces are written to the ignored `test-results` directory in this project. All fixtures and keys are fake.