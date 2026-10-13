import { useState } from "react";
import { BarChart3, Bell, MessageSquare, Users } from "lucide-react";
import FeedbackDialog from "./components/FeedbackDialog";

export default function App() {
  const [feedbackOpen, setFeedbackOpen] = useState(false);
  return (
    <>
      <nav className="navbar">
        <div className="brand"><span className="brand-mark">D</span> Dashboard</div>
        <div className="nav-links"><a href="#overview">Overview</a><a href="#activity">Activity</a><button type="button" className="feedback-nav" onClick={() => setFeedbackOpen(true)}><MessageSquare size={17}/> Feedback</button></div>
      </nav>
      <main className="container" id="overview">
        <div className="welcome"><p className="eyebrow">Workspace / Overview</p><h1>Welcome back 👋</h1><p>Here's what's happening in your workspace today.</p></div>
        <div className="metrics">
          <article className="metric"><BarChart3/><p>Performance</p><strong>84.6%</strong><small>+12.8% this month</small></article>
          <article className="metric"><Users/><p>Active users</p><strong>12,480</strong><small>+6.4% this month</small></article>
          <article className="metric"><Bell/><p>Notifications</p><strong>28</strong><small>Latest workspace updates</small></article>
        </div>
        <section className="activity" id="activity"><h2>Recent activity</h2><p>This is sample dashboard content. Open Feedback in the navbar, select Screenshot, and capture this page without the feedback dialog appearing in the image.</p>
          {["Project status updated", "Dashboard analytics refreshed", "New members invited", "Weekly report generated"].map((value,i)=><div className="activity-row" key={value}><span className="activity-dot"/><span>{value}</span><small>{i+1} hour(s) ago</small></div>)}
        </section>
      </main>
      <FeedbackDialog feedbackOpen={feedbackOpen} setFeedbackOpen={setFeedbackOpen} userEmail="demo@example.com" />
    </>
  );
}
