# Smart Test

Run dotnet tests with filtered, readable output.

## Usage

User will optionally provide a filter pattern. You should:

1. Run the smart test tool:
   ```bash
   python3 tools/smart_test.py [optional-filter]
   ```

2. Show the compact summary to the user

3. If tests fail, analyze the detailed output and fix issues

## Examples

- `/test` - Run all tests, show summary
- `/test ParameterSweeper` - Run only ParameterSweeper tests
- `/test BoundingBox` - Run only BoundingBox-related tests

The tool filters output to show only summary instead of overwhelming 1193 test results!
