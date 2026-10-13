import { useEffect, useRef, useState } from "react";
import html2canvas from "html2canvas-pro";
import { Camera, Download, Eye, Send, Trash2, X } from "lucide-react";

export interface FeedbackProps {
  feedbackOpen: boolean;
  setFeedbackOpen: (open: boolean) => void;
  userEmail: string;
  onSubmit?: (payload: { email: string; message: string; screenshot: File | null }) => Promise<void>;
}
const MAX_BYTES = 5 * 1024 * 1024;
const nextPaint = () => new Promise<void>((resolve) =>
  requestAnimationFrame(() => requestAnimationFrame(() => resolve()))
);

export default function FeedbackDialog({ feedbackOpen, setFeedbackOpen, userEmail, onSubmit }: FeedbackProps) {
  const [message, setMessage] = useState("");
  const [screenshotMode, setScreenshotMode] = useState(false);
  const [capturing, setCapturing] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [screenshot, setScreenshot] = useState<File | null>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [expanded, setExpanded] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const toolbarRef = useRef<HTMLDivElement>(null);
  const workingRef = useRef(false);

  useEffect(() => {
    if (!screenshot) { setPreviewUrl(null); return; }
    const url = URL.createObjectURL(screenshot);
    setPreviewUrl(url);
    return () => URL.revokeObjectURL(url);
  }, [screenshot]);

  useEffect(() => {
    if (!screenshotMode) return;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape" && !workingRef.current) setScreenshotMode(false);
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [screenshotMode]);

  if (!feedbackOpen) return null;

  const takeScreenshot = async () => {
    if (workingRef.current) return;
    workingRef.current = true;
    setCapturing(true);
    setError(null);
    try {
      if (toolbarRef.current) toolbarRef.current.style.visibility = "hidden";
      await nextPaint();
      // Capture only the visible page region, preserving its current scroll position.
      const canvas = await html2canvas(document.documentElement, {
        backgroundColor: "#ffffff",
        useCORS: true,
        allowTaint: false,
        logging: false,
        scale: Math.min(window.devicePixelRatio || 1, 2),
        x: window.scrollX,
        y: window.scrollY,
        width: window.innerWidth,
        height: window.innerHeight,
        ignoreElements: (element) => element.hasAttribute("data-screenshot-ignore"),
      });
      const blob = await new Promise<Blob>((resolve, reject) =>
        canvas.toBlob((value) => value ? resolve(value) : reject(new Error("Unable to encode PNG")), "image/png")
      );
      if (blob.size > MAX_BYTES) throw new Error("Screenshot exceeds the 5 MiB attachment limit.");
      setScreenshot(new File([blob], `Feedback-${Date.now()}.png`, { type: "image/png" }));
      setScreenshotMode(false);
    } catch (cause: unknown) {
      setError(cause instanceof Error ? cause.message : "Screenshot capture failed.");
      setScreenshotMode(false);
    } finally {
      if (toolbarRef.current) toolbarRef.current.style.visibility = "";
      workingRef.current = false;
      setCapturing(false);
    }
  };

  const sendFeedback = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!message.trim()) return;
    if (!onSubmit) { setError("Connect an onSubmit handler to your feedback API before sending."); return; }
    setSubmitting(true);
    setError(null);
    try {
      await onSubmit({ email: userEmail, message: message.trim(), screenshot });
      setMessage("");
      setScreenshot(null);
      setFeedbackOpen(false);
    } catch (cause: unknown) {
      setError(cause instanceof Error ? cause.message : "Feedback submission failed.");
    } finally { setSubmitting(false); }
  };

  return (
    <>
      {!screenshotMode ? (
        <div className="feedback-overlay" data-screenshot-ignore>
          <section role="dialog" aria-modal="true" aria-labelledby="feedback-title" className="feedback-dialog">
            <header className="feedback-header"><div><h2 id="feedback-title">Share your feedback</h2><p>Tell us what you think or report an issue.</p></div>
              <button type="button" aria-label="Close feedback" className="icon-button" onClick={() => setFeedbackOpen(false)}><X size={20}/></button>
            </header>
            <form onSubmit={sendFeedback}>
              <label htmlFor="feedback-message">Your comments</label>
              <textarea id="feedback-message" required maxLength={1024} rows={5} value={message} onChange={event => setMessage(event.target.value)} placeholder="Describe your issue or suggestion..." />
              <div className="attachment-heading"><strong>Screenshot</strong><span>Optional · PNG · max 5 MiB</span></div>
              {!screenshot ? (
                <button type="button" className="capture-button" onClick={() => { setError(null); setScreenshotMode(true); }}><Camera size={18}/> Select screenshot</button>
              ) : (
                <div className="attachment-card">
                  {previewUrl && <img src={previewUrl} alt="Captured page" className="thumbnail" />}
                  <div className="attachment-actions">
                    <button type="button" onClick={() => setExpanded(true)}><Eye size={15}/> Preview</button>
                    <button type="button" onClick={() => setScreenshotMode(true)}><Camera size={15}/> Retake</button>
                    {previewUrl && <a href={previewUrl} download={screenshot.name}><Download size={15}/> Save</a>}
                    <button type="button" onClick={() => setScreenshot(null)}><Trash2 size={15}/> Remove</button>
                  </div>
                </div>
              )}
              {error && <p role="alert" className="error">{error}</p>}
              <button type="submit" disabled={submitting || !message.trim()} className="submit-button"><Send size={16}/> {submitting ? "Sending..." : "Submit feedback"}</button>
            </form>
          </section>
        </div>
      ) : (
        <div ref={toolbarRef} data-screenshot-ignore className="screenshot-toolbar">
          <span>{capturing ? "Capturing..." : "Capture the visible page"}</span>
          <button type="button" disabled={capturing} onClick={() => setScreenshotMode(false)}>Cancel</button>
          <button type="button" disabled={capturing} className="primary" onClick={takeScreenshot}><Camera size={16}/> Capture screenshot</button>
        </div>
      )}
      {expanded && previewUrl && !screenshotMode && (
        <div className="image-lightbox" data-screenshot-ignore role="dialog" aria-modal="true" aria-label="Screenshot preview" onClick={() => setExpanded(false)}>
          <button className="lightbox-close" type="button" aria-label="Close preview" onClick={() => setExpanded(false)}><X/></button>
          <img src={previewUrl} alt="Full screenshot" onClick={event => event.stopPropagation()} />
        </div>
      )}
    </>
  );
}
