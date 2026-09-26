export const GITHUB = "https://github.com/adarshnub";
export const projectCategories = ["All projects", "AI & automation", "Developer tools", "Web products", "3D & games"] as const;
export type ProjectCategory = typeof projectCategories[number];
export type DeveloperProject = {
  name: string; repo: string; category: Exclude<ProjectCategory, "All projects">;
  kind: string; mark: string; description: string; detail: string; stack: string[];
};

// Public documentation and source references: docs/developer-project-sources.md.
export const developerProjects: DeveloperProject[] = [
  {
    name: "Autobots", repo: "origin-autobots", category: "AI & automation", kind: "Desktop assistant", mark: "Au",
    description: "A Windows desktop assistant that turns an owner's task into visible, step-by-step actions.",
    detail: "A floating pilot, voice input, and a local STOP control keep the person at the center of the workflow.",
    stack: ["C#", "Avalonia", "Python", "AWS"],
  },
  {
    name: "FrameOS", repo: "FrameOS", category: "AI & automation", kind: "Video editing platform", mark: "Fr",
    description: "An API-first video editing platform built around a precise editing document and reversible transactions.",
    detail: "Gives agents an editing kernel, previews, caption tools, SDKs, and MCP access to work with video.",
    stack: ["TypeScript", "MLT", "FFmpeg", "MCP"],
  },
  {
    name: "Tracefy", repo: "tracefy", category: "Developer tools", kind: "Debugging prototype", mark: "Tr",
    description: "Connects browser errors, terminal failures, and relevant source code in one debugging timeline.",
    detail: "A VS Code extension and Chrome bridge bring context into AI diagnosis, with patch previews for review.",
    stack: ["TypeScript", "VS Code", "Chrome", "OpenAI"],
  },
  {
    name: "Agent OS", repo: "agent-os", category: "AI & automation", kind: "Local agent runtime", mark: "Ao",
    description: "A local workspace where AI agents share context, exchange messages, and use scoped tools.",
    detail: "Combines a desktop interface with task previews, an audit trail, and rollback snapshots.",
    stack: ["TypeScript", "React", "Express", "Electron"],
  },
  {
    name: "Org Context", repo: "org-context", category: "AI & automation", kind: "Team knowledge", mark: "Oc",
    description: "Team workspaces with real-time group chat and an /ask command for finding answers in past conversations.",
    detail: "Retrieves relevant messages and attaches source references to answers, with workspace-level model selection.",
    stack: ["Next.js", "Supabase", "pgvector", "Cohere"],
  },
  {
    name: "Textify", repo: "textify", category: "AI & automation", kind: "Audio transcription CLI", mark: "Tx",
    description: "Turns audio recordings into speaker-labeled transcripts with word-level timestamps.",
    detail: "Exports structured JSON and supports CPU or GPU processing through a compact Python command-line tool.",
    stack: ["Python", "WhisperX", "Docker"],
  },
  {
    name: "Conceptly", repo: "Conceptly", category: "AI & automation", kind: "Learning app · MVP", mark: "Co",
    description: "An interactive AI Foundations course built from short lessons, practice, and useful answer feedback.",
    detail: "Sequential lessons, XP, streaks, and an optional AI coach give the learning journey a sense of progress.",
    stack: ["Next.js", "TypeScript", "Supabase", "OpenAI"],
  },
  {
    name: "Plotverse", repo: "plotverse", category: "AI & automation", kind: "Real-estate assistant", mark: "Pv",
    description: "A personal automation studio for researching properties and matching them with potential clients.",
    detail: "Organizes real-estate workflows around multiple agents, with local data storage and optional cloud integrations.",
    stack: ["TypeScript", "OpenAI", "Supabase"],
  },
  {
    name: "Rich Text Editor", repo: "rich-text-editor-lib", category: "Developer tools", kind: "React component library", mark: "Rt",
    description: "A reusable React editor with text formatting, lists, links, and a customizable toolbar.",
    detail: "Includes keyboard shortcuts and styling hooks so the editing experience can fit the surrounding product.",
    stack: ["React", "TypeScript", "Lucide"],
  },
  {
    name: "ParkEasy", repo: "pay-and-park-automation", category: "Web products", kind: "Parking management", mark: "Pk",
    description: "A parking workspace for vehicle check-in, checkout, occupancy, and duration-based billing.",
    detail: "Number-plate recognition assists entry, while staff review the captured plate before confirming a visit.",
    stack: ["Next.js", "Supabase", "Python", "OCR"],
  },
  {
    name: "Tripundo", repo: "tripundo", category: "Web products", kind: "Travel communities", mark: "Tu",
    description: "A community travel app for discovering destinations, finding people, and planning trips together.",
    detail: "Brings destination groups, chat, shared trip plans, and a trip-story studio into one experience.",
    stack: ["Next.js", "TypeScript", "React"],
  },
  {
    name: "Kingdom MMO", repo: "kingdom-mmo", category: "3D & games", kind: "Strategy game prototype", mark: "Km",
    description: "The foundation of a mobile multiplayer strategy game with a Unity client and an authoritative server.",
    detail: "The first server slice covers building queues, map views, marches, and deterministic combat resolution.",
    stack: ["Unity", "C#", "NestJS", "TypeScript"],
  },
  {
    name: "PDF Editor", repo: "pdf-editor", category: "Web products", kind: "Document utility", mark: "Pd",
    description: "A web app for uploading, viewing, and saving PDFs, then extracting selected pages into a download.",
    detail: "Combines an authenticated React interface with an Express backend and MongoDB persistence.",
    stack: ["React", "Express", "MongoDB", "Tailwind CSS"],
  },
  {
    name: "Find My Doctor", repo: "findmydoctor_doctor_interface_frontend_v1", category: "Web products", kind: "Healthcare interface", mark: "Md",
    description: "A doctor-facing interface with hospital, doctor, and patient areas for appointment-related workflows.",
    detail: "Includes a weekly availability form with time-slot selection and scheduling validation.",
    stack: ["Next.js", "TypeScript", "Formik", "Yup"],
  },
  {
    name: "Freelance Hub", repo: "freelance-webapp-frontend-v1", category: "Web products", kind: "Marketplace frontend", mark: "Fh",
    description: "A marketplace interface for browsing freelance projects and jobs, and putting together a project brief.",
    detail: "Explores listing, detail, and posting flows through a responsive Next.js frontend.",
    stack: ["Next.js", "TypeScript", "React"],
  },
  {
    name: "Origin Studio", repo: "origin-frontend-v1", category: "Web products", kind: "Creative studio · In progress", mark: "Os",
    description: "A workspace for creative instruments, with a Frame studio and video-editing project screens.",
    detail: "The frontend brings together account flows, project navigation, an editor surface, and a costs area.",
    stack: ["Next.js", "TypeScript", "React"],
  },
];
