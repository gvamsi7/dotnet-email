# Feedback Screenshot (React 19 + TypeScript)

Standalone demo inside the existing dotnet-email repository. Does not modify the .NET email service.

## Run

```bash
cd def
npm install
npm run dev
npm run build
```

## Behavior

1. Select **Feedback** in the navbar.
2. Type feedback and select **Select screenshot**.
3. The feedback dialog and backdrop disappear; the app behind them stays mounted.
4. Select **Capture screenshot** (or Cancel). The toolbar is hidden while capturing.
5. The original feedback form returns with a PNG attachment preview, Retake, Save, and Remove.

## Integrate into your app

Import `src/components/FeedbackDialog.tsx` and use your existing state:

```tsx
<FeedbackDialog
  feedbackOpen={feedbackOpen}
  setFeedbackOpen={setFeedbackOpen}
  userEmail={user?.email ?? ""}
  onSubmit={async ({email, message, screenshot}) => {
    const form = new FormData();
    form.append("email", email);
    form.append("message", message);
    if (screenshot) form.append("screenshot", screenshot);
    const response = await fetch("/api/feedback", {method:"POST",body:form});
    if (!response.ok) throw new Error("Unable to submit feedback");
  }}
/>
```

Change the endpoint and field names to match your backend. **The demo does not send email by itself**: submission is intentionally disabled until an `onSubmit` handler is provided. Never hardcode SMTP credentials in frontend code.

## Notes

- Uses `html2canvas-pro` for a viewport screenshot, not browser screen capture; no screen-share permission prompt.
- Some cross-origin images, videos, iframes and complex CSS may not be faithfully rendered.
- The feedback modal remains mounted as React component state; only its UI is conditionally hidden, keeping user-entered feedback intact.
- PNG attachment limit: 5 MiB.
- If integrating into an existing modal library, ensure its separately portaled backdrop is also hidden during screenshot mode.
