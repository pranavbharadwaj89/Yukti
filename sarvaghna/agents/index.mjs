// sarvaghna/agents/index.mjs
// Exports and registers the 5-Agent Sarvaghna Crew

import { OrchestratorAgent } from './orchestrator.mjs';
import { AnalystAgent } from './analyst.mjs';
import { CoderAgent } from './coder.mjs';
import { TesterAgent } from './tester.mjs';
import { ReviewerAgent } from './reviewer.mjs';

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
  }
];

export {
  OrchestratorAgent,
  AnalystAgent,
  CoderAgent,
  TesterAgent,
  ReviewerAgent
};
