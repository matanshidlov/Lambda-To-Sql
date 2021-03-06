﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace Lambda_To_Sql
{
    public class QueryBuilder<T> where T : class
    {
        public QueryBuilder()
        {
            _where = null;
            _groupBy = new List<string>();
            _orderBy = new List<string>();
            _projection = new List<string>();
            _sum = new List<string>();
            _count = new List<string>();
            _join = new List<KeyValuePair<Type, Tuple<string, string>>>();
        }

        private Expression<Func<T, bool>> _where { get; set; }
        private List<string> _groupBy { get; set; }
        private List<string> _orderBy { get; set; }
        private List<string> _projection { get; set; }
        private List<string> _sum { get; set; }
        private List<string> _count { get; set; }
        private List<KeyValuePair<Type,Tuple<string,string>>> _join  { get; set; }
        private int _limit { get; set; }
        private int _offset { get; set; }


        public string Select()
        {
            return string.Format("SELECT {0}{1}{2} FROM {3} {4} {5} {6} {7} {8} {9}", Projection(), Sum(), Count(),
                typeof (T).Name,Join(), Where(), GroupBy(), OrderBy(), Offset(), Limit());
        }

        public QueryBuilder<T> Where(Expression<Func<T, bool>> expression)
        {
            _where = _where == null
                ? expression
                : Expression.Lambda<Func<T, bool>>(
                    Expression.AndAlso(_where.Body, expression.Body),
                    _where.Parameters);
            return this;
        }

        public string Where()
        {
            var where = ConvertExpressionToString(_where.Body);
            return string.IsNullOrEmpty(where) ? string.Empty : string.Format("WHERE {0}", where);
        }

        public QueryBuilder<T> Join<J>(Expression<Func<T, object>> left, Expression<Func<J, object>> right)
        {
            _join.Add(new KeyValuePair<Type, Tuple<string, string>>(typeof (J),
                new Tuple<string, string>(ConvertExpressionToString(left), ConvertExpressionToString(right))));
            return this;
        }

        public string Join()
        {
            return string.Join(" ",
                _join.Select(
                    x =>
                        string.Format("JOIN {0} ON {1}.{2}={3}.{4}", x.Key.Name, typeof (T).Name, x.Value.Item1,
                            x.Key.Name, x.Value.Item2)));
        }

        public QueryBuilder<T> GroupBy(params Expression<Func<T, object>>[] expression)
        {
            _groupBy.AddRange(expression.Select(ConvertExpressionToString));
            return this;
        }

        public string GroupBy()
        {
            return _groupBy.Any() ? string.Format("GROUP BY {0}", string.Join(",", _groupBy)) : string.Empty;
        }

        public QueryBuilder<T> OrderBy(params Expression<Func<T, object>>[] expression)
        {
            _orderBy.AddRange(expression.Select(ConvertExpressionToString));
            return this;
        }

        public string OrderBy()
        {
            return _orderBy.Any() ? string.Format("ORDER BY {0}", string.Join(",", _orderBy)) : string.Empty;
        }


        public QueryBuilder<T> Limit(int limit)
        {
            _limit = limit;
            return this;
        }

        public string Limit()
        {
            return string.Format("LIMIT {0}", _limit);
        }

        public QueryBuilder<T> Offset(int offset)
        {
            _offset = offset;
            return this;
        }

        public string Offset()
        {
            return string.Format("OFFSET {0}", _offset);
        }


        public QueryBuilder<T> Sum(params Expression<Func<T, object>>[] expression)
        {
            _sum.AddRange(expression.Select(ConvertExpressionToString));
            return this;
        }
        public string Sum()
        {
            return string.Join(string.Empty, _sum.Select(x => string.Format(",SUM({0}) AS {0}", x)));
        }
        public QueryBuilder<T> Count(params Expression<Func<T, object>>[] expression)
        {
            _count.AddRange(expression.Select(ConvertExpressionToString));
            return this;
        }
        public string Count()
        {
            return string.Join(string.Empty, _count.Select(x => string.Format(",COUNT({0}) AS {0}", x)));
        }

        public List<string> Projections()
        {
            var properties = _groupBy.Any() ? _groupBy : Properties();
            if (_projection.Any())
            {
                _projection = _projection.Intersect(properties).ToList();
            }
            return _projection.Any() ? _projection : properties;
        }

        public string Projection()
        {
            return string.Join(",", Projections());
        }

        private List<string> Properties()
        {
            var properties = typeof(T).GetProperties().Where(prop => Attribute.IsDefined(prop, typeof(CustomColumn)));
            return properties.Select(prop => prop.Name).ToList();
        }

        private static string ConvertExpressionToString(Expression body)
        {
            if (body == null)
            {
                return string.Empty;
            }
            if (body is ConstantExpression)
            {
                return ValueToString(((ConstantExpression)body).Value);
            }
            if (body is MemberExpression)
            {
                var member = ((MemberExpression)body);
                if (member.Member.MemberType == MemberTypes.Property)
                {
                    return member.Member.Name;
                }
                var value = GetValueOfMemberExpression(member);
                if (value is IEnumerable)
                {
                    var sb = new StringBuilder();
                    foreach (var item in value as IEnumerable)
                    {
                        sb.AppendFormat("{0},", ValueToString(item));
                    }
                    return sb.Remove(sb.Length - 1, 1).ToString();
                }
                return ValueToString(value);
            }
            if (body is UnaryExpression)
            {
                return ConvertExpressionToString(((UnaryExpression)body).Operand);
            }
            if (body is BinaryExpression)
            {
                var binary = body as BinaryExpression;
                return string.Format("({0}{1}{2})", ConvertExpressionToString(binary.Left),
                    ConvertExpressionTypeToString(binary.NodeType),
                    ConvertExpressionToString(binary.Right));
            }
            if (body is MethodCallExpression)
            {
                var method = body as MethodCallExpression;
                return string.Format("({0} IN ({1}))", ConvertExpressionToString(method.Arguments[0]),
                    ConvertExpressionToString(method.Object));
            }
            if (body is LambdaExpression)
            {
                return ConvertExpressionToString(((LambdaExpression)body).Body);
            }
            return "";
        }

        private static string ValueToString(object value)
        {
            if (value is string)
            {
                return string.Format("'{0}'", value);
            }
            if (value is DateTime)
            {
                return string.Format("'{0:yyyy-MM-dd HH:mm:ss}'", value);
            }
            return value.ToString();
        }

        private static object GetValueOfMemberExpression(MemberExpression member)
        {
            var objectMember = Expression.Convert(member, typeof(object));
            var getterLambda = Expression.Lambda<Func<object>>(objectMember);
            var getter = getterLambda.Compile();
            return getter();
        }

        private static string ConvertExpressionTypeToString(ExpressionType nodeType)
        {
            switch (nodeType)
            {
                case ExpressionType.And:
                    return " AND ";
                case ExpressionType.AndAlso:
                    return " AND ";
                case ExpressionType.Or:
                    return " OR ";
                case ExpressionType.OrElse:
                    return " OR ";
                case ExpressionType.Not:
                    return "NOT";
                case ExpressionType.NotEqual:
                    return "!=";
                case ExpressionType.Equal:
                    return "=";
                case ExpressionType.GreaterThan:
                    return ">";
                case ExpressionType.GreaterThanOrEqual:
                    return ">=";
                case ExpressionType.LessThan:
                    return "<";
                case ExpressionType.LessThanOrEqual:
                    return "<=";
                default:
                    return "";
            }
        }
    }

    
}
