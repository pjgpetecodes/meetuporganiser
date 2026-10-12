---
name: social-card-designer
description: Skill for designing and validating 1280x720 social card graphics and PPTX presentations.
---

# Social Card Designer Skill

## When to Use
Use this skill when modifying image generation templates, testing SkiaSharp layouts, or adjusting typography.

## Design Specifications
1. **Dimensions**: Standard 16:9 ratio (1280px width by 720px height) matching Twitter, LinkedIn, and BlueSky social preview cards.
2. **Color Palette**:
   - Primary: Group brand color (`#512BD4` for .NET Purple, `#005596` for .NET Notts).
   - Secondary: Accent brand color (`#FDB813` or `#0078D4`).
   - Dark background overlay (`#0A1128` or `#1E1E2E`) with a 45-degree linear gradient.
3. **Typography**:
   - Header / Date: Bold sans-serif, high contrast white/light gray.
   - Title: Bold, 36-42pt, clamped to 2 lines maximum with ellipsis if overflowing.
   - Speaker Names: 24-28pt bold, paired with talk titles in italicized accent color.
4. **Speaker Avatars**:
   - 1 Speaker: Centered or right-aligned 260px circular avatar with a 4px secondary accent border.
   - 2 Speakers: Twin 200px circular avatars with balanced horizontal spacing.
