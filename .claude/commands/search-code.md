# Search Code (Semantic)

Use the semantic search tool to find relevant code in the codebase.

## Usage

User will provide a search query. You should:

1. Run the semantic search tool:
   ```bash
   python3 tools/semantic_search.py "<query>"
   ```

2. Show the results to the user

3. Optionally read the most relevant files

## Examples

- `/search-code ViewModel for parameter sweeping`
- `/search-code where is bounding box calculation`
- `/search-code test files for components`

The tool uses AI embeddings to find semantically similar code, much better than grep!
