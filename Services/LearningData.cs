using System.Collections.Generic;

namespace ExploreDB.Services
{
    public class LearningTopic
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string ColorClass { get; set; } = string.Empty;
        public string ContentHtml { get; set; } = string.Empty;
    }

    public static class LearningData
    {
        public static List<LearningTopic> GetTopics()
        {
            return new List<LearningTopic>
            {
                new LearningTopic
                {
                    Id = "join_start",
                    Title = "Which Table to Start With?",
                    Category = "JOINs",
                    Icon = "bi-journal-text",
                    ColorClass = "text-primary",
                    ContentHtml = @"
                        <h5 class=""fw-bold mb-3 text-primary"">Rule of Thumb</h5>
                        <div class=""text-light lh-lg"" style=""font-size: 1.05rem;"">
                            <p>In highly connected databases, choosing the right starting table for your query is crucial for performance:</p>
                            <ul class=""mb-0"">
                                <li class=""mb-2""><strong>Most Selective:</strong> Always start with the table that has the fewest matching rows <i class=""text-secondary"">after</i> your WHERE clause is applied.</li>
                                <li class=""mb-2""><strong>Reporting:</strong> Start with the fact table (e.g., Transactions, Orders, Events).</li>
                                <li><strong>Lookups:</strong> Start with the dimension table that contains your primary filtering criteria.</li>
                            </ul>
                        </div>"
                },
                new LearningTopic
                {
                    Id = "join_cross",
                    Title = "The Danger of Cross Joins",
                    Category = "JOINs",
                    Icon = "bi-journal-text",
                    ColorClass = "text-danger",
                    ContentHtml = @"
                        <h5 class=""fw-bold mb-3 text-danger"">Cartesian Products</h5>
                        <div class=""text-light lh-lg"" style=""font-size: 1.05rem;"">
                            <p>If you join two tables together without specifying an <code class=""bg-black px-2 py-1 rounded text-danger"">ON</code> condition (or without a valid Foreign Key path), the database will generate a <strong>Cross Join</strong>.</p>
                            <div class=""alert bg-black border-start border-danger border-4 text-light mt-4 rounded-0 shadow-sm"">
                                <strong class=""text-danger"">Why is this bad?</strong><br/>
                                If Table A has 1,000 rows and Table B has 1,000 rows, a cross join multiplies them, resulting in <strong class=""text-white"">1,000,000 rows</strong> in your output! This can crash your application or severely lag the database.
                            </div>
                        </div>"
                },
                new LearningTopic
                {
                    Id = "debug_print",
                    Title = "Debugging: Using PRINT",
                    Category = "Stored Procedures",
                    Icon = "bi-journal-text",
                    ColorClass = "text-info",
                    ContentHtml = @"
                        <h5 class=""fw-bold mb-3 text-info"">PRINT Statements</h5>
                        <div class=""text-light lh-lg"" style=""font-size: 1.05rem;"">
                            <p>The simplest way to debug a complex Stored Procedure is by using <code class=""bg-black px-2 py-1 rounded text-info"">PRINT</code> statements to track the execution flow and variable states.</p>
                            <pre class=""bg-black text-success p-4 rounded mt-4 shadow-sm""><code style=""font-family: 'Consolas', monospace;"">DECLARE @StepName NVARCHAR(50) = 'Validation';
PRINT 'Starting step: ' + @StepName;

<span class=""text-secondary"">-- Your code here...</span>

PRINT 'Rows affected: ' + CAST(@@ROWCOUNT AS NVARCHAR(10));</code></pre>
                        </div>"
                },
                new LearningTopic
                {
                    Id = "adv_window",
                    Title = "Window Functions (OVER)",
                    Category = "Advanced SQL",
                    Icon = "bi-journal-text",
                    ColorClass = "text-warning",
                    ContentHtml = @"
                        <h5 class=""fw-bold mb-3 text-warning"">The OVER() Clause</h5>
                        <div class=""text-light lh-lg"" style=""font-size: 1.05rem;"">
                            <p>Window functions allow you to perform calculations across a set of rows related to the current row, without actually collapsing the rows like <code class=""bg-black px-2 py-1 rounded text-warning"">GROUP BY</code> does.</p>
                            <ul class=""mb-0 mt-4"">
                                <li class=""mb-3""><code class=""bg-black px-2 py-1 rounded text-warning"">ROW_NUMBER() OVER(PARTITION BY Department ORDER BY Salary DESC)</code><br/><span class=""text-secondary"">Ranks employees within each department.</span></li>
                                <li><code class=""bg-black px-2 py-1 rounded text-warning"">SUM(Sales) OVER(ORDER BY Date)</code><br/><span class=""text-secondary"">Creates a running total over time.</span></li>
                            </ul>
                        </div>"
                },
                new LearningTopic
                {
                    Id = "adv_cte",
                    Title = "Common Table Expressions (CTE)",
                    Category = "Advanced SQL",
                    Icon = "bi-journal-text",
                    ColorClass = "text-success",
                    ContentHtml = @"
                        <h5 class=""fw-bold mb-3 text-success"">WITH Statements</h5>
                        <div class=""text-light lh-lg"" style=""font-size: 1.05rem;"">
                            <p>A CTE allows you to define a temporary result set that you can reference within a SELECT, INSERT, UPDATE, or DELETE statement. It makes queries vastly more readable.</p>
                            <pre class=""bg-black text-success p-4 rounded mt-4 shadow-sm""><code style=""font-family: 'Consolas', monospace;""><span class=""text-info"">WITH</span> ActiveUsers <span class=""text-info"">AS</span> (
    <span class=""text-primary"">SELECT</span> UserID, Name 
    <span class=""text-primary"">FROM</span> Users 
    <span class=""text-primary"">WHERE</span> IsActive = 1
)
<span class=""text-primary"">SELECT</span> * 
<span class=""text-primary"">FROM</span> ActiveUsers 
<span class=""text-primary"">WHERE</span> Name <span class=""text-info"">LIKE</span> 'A%';</code></pre>
                        </div>"
                }
            };
        }
    }
}
