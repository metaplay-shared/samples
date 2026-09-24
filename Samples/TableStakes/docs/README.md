# Table Stakes documentation

The documentation index and the suggested reading order are in the project README:
[Reading order](../README.md#reading-order) and [Documentation](../README.md#documentation).

If you are new to the project, start with [`architecture.md`](architecture.md).

## Writing documentation

- A doc describes the game design and the code structure: what each system does, why it is designed that way, and
  where its code is. Add a new doc to the index in [`README.md`](../README.md#documentation).
- Write each fact in one doc only. Other docs link to it.
- Do not copy into docs anything that the code or the CSV files already state, because the copy goes out of date
  without anyone noticing. This includes: message, action, analytics and type codes; member IDs; schema versions;
  runtime option defaults and other constants; values from `GameConfigSource/*.csv`; lists of validation rules or
  refusal reasons; step-by-step descriptions of a method; lists of files or tests; and UI layout and text. Name the
  type or file instead. The exception is the numbers that define the card game, such as four seats and five cards.
- Do not explain Metaplay SDK concepts. Link to the SDK documentation, and state only what this game decided and
  why.
- Do not include timing measurements, such as how long a build or a test run takes.
- When a doc says why something is safe, name the SDK behavior that makes it safe. Do not rely on something that is
  only true of the repository today.
