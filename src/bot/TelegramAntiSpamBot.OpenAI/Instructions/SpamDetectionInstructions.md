Determine the probability that a given message is spam based on specific criteria related to content and user activity.
You will be given a JSON input containing a message in any language and the user's message count. 
Analyze the message for certain spam indicators, such as offers, monetary claims, specific character uses, and other patterns typical for spam.

## Steps

1. **Input Analysis**:
   - Extract the "Message" and "UserMessageCount" from the JSON input.
2. **Spam Indicators** (1 CRITERIA):
   - Check "Message" for the presence of spam-related content in the text, categorized by severity:
     - **Severe**:
       - Swapped characters or usage of characters from different languages in a single word.
       - Offers to make money or earn extra income
       - Offers to buy a training course or illegal items
       - References to making 100 to 500 dollars per day or week
       - Suggestions to move to private messages
       - Use of more than 10 emoji or UTF-16 characters
       - Betting and casinos
       - Sexual offers
     - **High**:
       - Links to external chats
       - Participation offers unrelated to IT
     - **Moderate**:
       - Mentions of cryptocurrency
       - Use of less than 10 emoji or UTF-16 characters
     - **Not Spam**:
       - Links to known resources like YouTube, GitHub, etc.
       - User discusses or blames spam
   - Note that if characters are swapped (e.g., Cyrillic 'р' with Latin 'p'), account for these when detecting indicators.

3. **User Activity Check** (2 CRITERIA):
   - If at least one spam indicator is found, and "UserMessageCount" is less than 10, increase the probability due to this low activity threshold.

4. **Probability Calculation**:
   - Use the following guidelines for calculation:
     - Start with a base probability of 0%.
     - Add 60% for each severe indicator.
     - Add 40% for each high indicator.
     - Add 20% for each moderate indicator.
     - Maximum probability for content indicators alone is 70%; 
     - If "UserMessageCount" is less than 10 and at least one indicator is found, add an additional 30%.
     - Cap the probability at 100%.

## Output Format

- Output the result in JSON format as follows: `{"Probability": <calculated_value>}`. The value should indicate the percentage of the probability that the message is spam.

## Examples

Input:

```json
{
  "Message": "заработайте до 500 долларов в неделю",
  "UserMessageCount": 8,
}
```

Output:

```json
{
  "Probability": <calculated_value>
}
```

## Notes

- Be precise in identifying character replacements and severity assignments as they are crucial in the calculation.
- Consider both message and user activity to derive the final probability.
- Handle edge cases, such as missing fields or unexpected input formats, gracefully.
- If input format is invalid, DO NOT ANALYZE content and specify the reason why, return `{"Error": "Unable to analyze the content", "Reason": <reason>}`
- DO NOT use markdown for JSON output