// sarvaghna/agents/index.mjs
// Exports and registers the 5-Agent Sarvaghna Crew

import { OrchestratorAgent } from './orchestrator.mjs';
import { AnalystAgent } from './analyst.mjs';
import { CoderAgent } from './coder.mjs';
import { TesterAgent } from './tester.mjs';
import { ReviewerAgent } from './reviewer.mjs';
import { BravitasAgent } from './bravitas.mjs';

export const CREW_METADATA = [
  {
    id: 'orchestrator',
    name: 'Orchestrator',
    color: '#9B8FE0',
    dark: '#7C6FCD',
    bg: '#EEEDFE',
    tagline: 'The boss — routes and decides',
    class: OrchestratorAgent
  },
  {
    id: 'analyst',
    name: 'Analyst',
    color: '#1D9E75',
    dark: '#0F6E56',
    bg: '#E1F5EE',
    tagline: 'Reads docs and extracts meaning',
    class: AnalystAgent
  },
  {
    id: 'coder',
    name: 'Coder',
    color: '#378ADD',
    dark: '#185FA5',
    bg: '#E6F1FB',
    tagline: 'Writes and runs real code',
    class: CoderAgent
  },
  {
    id: 'tester',
    name: 'Tester',
    color: '#EF9F27',
    dark: '#854F0B',
    bg: '#FAEEDA',
    tagline: 'Runs tests and reports results',
    class: TesterAgent
  },
  {
    id: 'reviewer',
    name: 'Reviewer',
    color: '#D85A30',
    dark: '#993C1D',
    bg: '#FAECE7',
    tagline: 'Scores quality and gives feedback',
    class: ReviewerAgent
  },
  {
    id: 'bravitas',
    name: 'Bravitas',
    color: '#2BB3A3',
    dark: '#17746A',
    bg: '#E3F7F4',
    // Working name only ("Call it as Bravitas for now") — not a final brand.
    // Unlike the other five, this one drives a real browser against the
    // real running frontend rather than reading a git diff, so it isn't
    // part of --crew's mission pipeline; it runs standalone via --bravitas
    // (see sarvaghna.mjs), including a merge-triggered run from
    // .github/workflows/sarvaghna.yml.
    tagline: 'Audits the rendered UI for accessibility and broken pages',
    class: BravitasAgent
  }
];

export {
  OrchestratorAgent,
  AnalystAgent,
  CoderAgent,
  TesterAgent,
  ReviewerAgent,
  BravitasAgent
};
