# Writing Style Guide Format

This document outlines the recommended format for creating style guides that work optimally with the Universa AI writing assistant. While the parser is flexible and can understand various formats, following these guidelines will ensure the best results.

## Table of Contents
1. [Document Structure](#document-structure)
2. [Section Headers](#section-headers)
3. [Critical Rules](#critical-rules)
4. [Writing Sample](#writing-sample)
5. [Vocabulary Management](#vocabulary-management)
6. [Rule Types and Formatting](#rule-types-and-formatting)
7. [Technical Guidelines](#technical-guidelines)
8. [Examples and Templates](#examples-and-templates)

---

## Document Structure

### Basic Layout
```markdown
# [Style Guide Title] - [Mandatory Indicator if applicable]

## Primary Section Name
- Rule or guideline
- Another rule with **emphasis** on key parts

## Secondary Section Name
### Subsection if needed
- More specific rules
```

### Recommended Section Order
1. Voice/Narrative Rules (most critical)
2. Writing Sample
3. Technical/Genre-Specific Rules
4. Dialogue Guidelines
5. Description Guidelines
6. Vocabulary/Repetition Rules
7. Optional/Flexible Guidelines

---

## Section Headers

### High-Priority Sections
Use these keywords in headers for automatic high-priority recognition:

```markdown
## Voice Rules - THESE MUST BE FOLLOWED
## Critical Guidelines
## Mandatory Style Elements
## ALWAYS/NEVER Rules
```

### Standard Sections
```markdown
## Narrative Perspective
## Dialogue Presentation
## Character Development
## Scene Description
## Pacing and Structure
```

---

## Critical Rules

### Formatting Critical Rules
To ensure rules are recognized as mandatory:

1. **Use Clear Imperatives**
   ```markdown
   - You MUST always maintain third-person limited perspective
   - NEVER use omniscient narration except for establishing shots
   - Authors should ALWAYS show character emotions through action
   - CRITICAL: Avoid head-hopping between characters
   ```

2. **Specify Limits Clearly**
   ```markdown
   - Use profession-specific metaphors with a maximum of one instance per scene
   - Limit technical jargon to no more than two terms per paragraph
   - Restrict flashbacks to one per chapter maximum
   ```

---

## Writing Sample

### Optimal Formatting
The writing sample is crucial for style matching. Format it clearly:

```markdown
# Writing Sample - THIS DEMONSTRATES THE TARGET STYLE

[Your sample text here - ideally 500-2000 words showing various elements like narrative, dialogue, description, and pacing]
```

### Alternative Headers (All Recognized)
- `## Writing Sample`
- `# Writing Example`
- `## Style Sample`
- `[Writing Sample]`

### Best Practices for Samples
1. Include varied elements (dialogue, action, description)
2. Show typical pacing and rhythm
3. Demonstrate character voice
4. Display preferred sentence structure variety

---

## Vocabulary Management

### Format for Alternatives
To help avoid repetition, format vocabulary alternatives clearly:

```markdown
### Vocabulary Alternatives
- For "said": replied, answered, asked, murmured, whispered, declared
- For "walked": strode, paced, wandered, hurried, strolled, marched
- For "looked": glanced, gazed, peered, studied, examined, observed
```

### Technical Term Management
```markdown
### Technical Terminology Guidelines
- Narration: Use accessible language, reserve jargon for character thoughts
- Dialogue: Technical terms acceptable between experts
- Exposition: Introduce technical concepts with brief explanation, then use common terms
```

---

## Rule Types and Formatting

### 1. Must-Follow Rules (Highest Priority)
```markdown
- MUST: Always maintain consistent POV within scenes
- ALWAYS: Use active voice in action sequences
- NEVER: Break the fourth wall
- CRITICAL: Each chapter must advance the main plot
```

### 2. Should-Follow Guidelines (High Priority)
```markdown
- Should prefer showing emotions through action over telling
- Recommended: Vary sentence length for better flow
- Prefer concrete details over abstract descriptions
```

### 3. Can-Consider Suggestions (Medium Priority)
```markdown
- May occasionally use fragments for emphasis
- Can include brief omniscient transitions between chapters
- Optional: Include sensory details beyond sight and sound
```

### 4. Restrictions and Limits
```markdown
- Avoid clichéd phrases (list specific examples)
- Limit adverbs to 2-3 per page
- Don't use filter words (felt, saw, heard) more than once per scene
- Restrict italics for emphasis to crucial moments only
```

---

## Technical Guidelines

### For Different Narrative Contexts
Structure technical guidelines by context:

```markdown
## Technical Language Usage

### In Narration:
- Keep technical terms minimal
- Always provide context for specialized vocabulary
- Use layman's terms when possible

### In Dialogue:
- Characters can use jargon appropriate to their expertise
- Natural speech patterns override technical accuracy
- Avoid info-dumping through dialogue

### In Internal Thoughts:
- Technical observations should feel natural to the character
- Brief technical thoughts during action are acceptable
- Don't overload POV with professional filter
```

---

## Examples and Templates

### Complete Mini Style Guide Template

```markdown
# Fiction Style Guide - Project Name

## Voice Rules - MUST BE FOLLOWED
- Maintain close third-person limited POV
- NEVER head-hop within scenes
- Show character emotions through physical reactions and actions
- Maximum of one metaphor related to character's profession per scene

## Writing Sample
[Insert 500-1500 word sample that demonstrates all key style elements]

## Narrative Guidelines
### Primary Perspective
Write in third person limited, staying deep in the POV character's consciousness

### Technical Term Handling
- Narration: Use accessible language
- Dialogue: Natural to character background
- Exposition: Brief introduction, then common terms

## Dialogue Presentation
- Use contractions in dialogue for natural flow
- Vary dialogue tags (but don't overdo it)
- Include action beats to break up long exchanges

## Description Rules
- Prioritize sensory details the POV character would notice
- Integrate setting with character action
- Limit descriptions to 2-3 key details per element

## Vocabulary Alternatives
- For "said": asked, replied, murmured, whispered (use sparingly)
- For "walked": moved, stepped, strode, hurried
- For "looked": glanced, studied, examined, watched

## Pacing Guidelines
- Vary sentence length: short for tension, longer for reflection
- One major event per scene
- Chapter breaks at moments of tension or change
```

---

## Additional Tips

### 1. Consistency Markers
Use consistent formatting throughout:
- Always use `-` or `•` for bullets (pick one)
- Be consistent with CAPS for emphasis
- Use the same header level for similar sections

### 2. Clear Examples
When possible, provide brief examples:
```markdown
- Avoid filter words
  Wrong: "She felt the cold wind on her face"
  Right: "Cold wind stung her face"
```

### 3. Specificity
Be specific rather than vague:
```markdown
Instead of: "Write with good pacing"
Use: "Alternate between scenes of 500-1000 words and scenes of 1500-2500 words"
```

### 4. Organization
Group related rules together:
- All POV rules in one section
- All dialogue rules in another
- Technical language rules grouped by context

---

## Parser-Friendly Patterns

The StyleGuideParser recognizes these patterns particularly well:

1. **Headers with importance indicators**: `## Section Name - MUST FOLLOW`
2. **Bullet points with clear imperatives**: `- ALWAYS use...`, `- NEVER include...`
3. **Specific limits**: `maximum of X instances`, `limit to Y per scene`
4. **Vocabulary lists**: `For "term": alternative1, alternative2, alternative3`
5. **Context-specific rules**: `In Narration:`, `In Dialogue:`, `In Description:`

---

## Final Notes

Remember: While following this format will optimize parser recognition, the AI system is designed to understand naturally written style guides. The most important thing is that your style guide clearly communicates your intentions. The parser will extract what it can, and the full text is always available as a fallback.

Focus on clarity and completeness rather than perfect formatting. A well-written style guide in any reasonable format will produce good results. 