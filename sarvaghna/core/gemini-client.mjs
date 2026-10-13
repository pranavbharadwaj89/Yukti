// sarvaghna/core/gemini-client.mjs
// Shared Gemini JSON-mode caller. Extracted from rules/semantic-reviewer.mjs
// (Tier 4) so every crew agent that wants real reasoning instead of
// regex/templates calls the model the same verified way, with the same
// graceful degradation when GEMINI_API_KEY is absent.

/**
 * Calls Gemini with a prompt and expects a JSON object back
 * (responseMimeType: 'application/json'). Never throws — callers always
 * get a result object and decide what to do when ok is false.
 *
 * @param {Object} params
 * @param {string} params.prompt
 * @param {string} [params.model]
 * @param {number} [params.temperature]
 * @param {number} [params.maxOutputTokens]
 * @returns {Promise<{ok: true, data: any} | {ok: false, reason: string}>}
 */
export async function callGeminiJson({ prompt, model = 'gemini-3.8-flash', temperature = 0.2, maxOutputTokens = 4096 }) {
  const apiKey = process.env.GEMINI_API_KEY;
  if (!apiKey) {
    return { ok: false, reason: 'GEMINI_API_KEY not configured.' };
  }

  const endpoint = `https://generativelanguage.googleapis.com/v1beta/models/${model}:generateContent?key=${apiKey}`;

  try {
    const response = await fetch(endpoint, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        contents: [{ role: 'user', parts: [{ text: prompt }] }],
        generationConfig: {
          temperature,
          maxOutputTokens,
          responseMimeType: 'application/json'
        }
      })
    });

    if (!response.ok) {
      const errText = await response.text();
      return { ok: false, reason: `Gemini API returned error ${response.status}: ${errText.substring(0, 200)}` };
    }

    const data = await response.json();
    const candidate = data.candidates?.[0];
    const candidateText = candidate?.content?.parts?.[0]?.text;
    if (!candidateText) {
      return { ok: false, reason: `Empty response received from AI model (finishReason: ${candidate?.finishReason || 'unknown'}).` };
    }

    try {
      return { ok: true, data: JSON.parse(candidateText) };
    } catch (parseErr) {
      return { ok: false, reason: `Model output was not valid JSON (finishReason: ${candidate?.finishReason || 'unknown'}): ${parseErr.message}` };
    }
  } catch (err) {
    return { ok: false, reason: `Gemini call failed: ${err.message}` };
  }
}
