using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace nji
{
    public class Constraint
    {
        public string Operator { get; set; }
        public Version Value { get; set; }

        public bool SatisfiedBy(string version)
        {
            int comparison = Version.Parse(version).CompareTo(Value);
            switch (Operator)
            {
                case "<": return comparison < 0;
                case "<=": return comparison <= 0;
                case "=": return comparison == 0;
                case ">=": return comparison >= 0;
                case ">": return comparison > 0;
                default: throw new Exception(string.Format("Invalid operator: {0}", Operator));
            }
        }

        public Constraint(string op, string value)
        {
            Value = Version.Parse(value); // throws if the value can't be parsed
            if (!(new[] { "<", "<=", "=", ">=", ">" }.Contains(op)))
            {
                throw new ArgumentException(string.Format("Operator '{0}' not understood", op));
            }

            Operator = op;
        }
    }
}