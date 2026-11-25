import sys
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from sdd import outcomes, check_pin, validate


def trx(outcome="Passed"):
    return ET.fromstring(f'''<TestRun><TestDefinitions><UnitTest id="a">
      <TestMethod className="UnitTests.Import.Reader" name="Reads"/></UnitTest>
      </TestDefinitions><Results><UnitTestResult testId="a" outcome="{outcome}"/>
      </Results></TestRun>''')


class AcceptanceTests(unittest.TestCase):
    def test_matches_exact_method_and_requires_all_rows_passed(self):
        self.assertEqual(outcomes(trx(), ["UnitTests.Import.Reader.Reads"]), ["passed"])
        for status in ["Failed", "NotExecuted"]:
            self.assertEqual(outcomes(trx(status), ["UnitTests.Import.Reader.Reads"]), ["failed"])
        self.assertEqual(outcomes(trx(), ["UnitTests.Import.Reader.Other"]), ["missing"])
        tree = trx()
        ET.SubElement(tree.find("Results"), "UnitTestResult", testId="a", outcome="Failed")
        self.assertEqual(outcomes(tree, ["UnitTests.Import.Reader.Reads"]), ["failed"])

    def test_pin_requires_one_current_declaration(self):
        pin = "f" * 40
        check_pin("SDD-Spec: " + pin, pin)
        for body in ["", "SDD-Spec: " + "a" * 40, ("SDD-Spec: " + pin + "\n") * 2]:
            with self.assertRaises(ValueError):
                check_pin(body, pin)

    def test_contract_requires_unique_criteria_and_safe_method_names(self):
        row = dict(id="GDS-01", behavior="Reader opens the file", test="UnitTests.Import.Reader.Reads")
        validate([row])
        for rows in [[], [row, row], [dict(row, behavior="")], [dict(row, test="X|Y")]]:
            with self.assertRaises(ValueError):
                validate(rows)
