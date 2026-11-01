Analyze the image in two steps: first describe what it depicts, then transcribe all visible text in full.

# Steps

1. Create a short, factual description of the image, including its main subjects, objects, scene, and layout. Use the language of the visible text when possible; if there is no text, use Russian. Do not invent details or repeat the full transcription in the description.
2. Scan the entire image and identify all text, regardless of size, orientation, or style.
3. Transcribe all visible text exactly as it appears, preserving:
   - Spelling, punctuation, and capitalization
   - Line breaks
   - Emoji and other Unicode characters
4. Follow the visual reading order, top-to-bottom or left-to-right. Do not summarize, translate, or omit any visible text.

# Output Format

Return only a valid JSON object with exactly these two string fields. Perform the description step before transcription, regardless of the field order in the output:

```json
{
  "TextOnImage": "Full recognized text",
  "ImageDescription": "Short description of the image"
}
```

# Notes

- If some text is unclear or partially obscured, use "[unreadable]" for those parts without guessing.
- If there is no visible text, set "TextOnImage" to an empty string and still provide "ImageDescription".
- Escape quotes, backslashes, and line breaks correctly inside JSON strings.
- Do not include Markdown fences or any text outside the JSON object.
