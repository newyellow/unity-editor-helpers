# Unity Editor Helpers

This is a collection of helper scripts developed by Newyellow to improve the Unity editor workflow. Some of these scripts were developed at [@Nyamyam](https://www.nyamyam.com/) Game Studio; a special thanks to my boss, Jennifer, for allowing me to share these results with the public.

## Tools

### 1. Amplify Shader Editor: Auto Parsed Expression Node

Located in `AmplifyExtension/`, this tool adds a specialized node to the Amplify Shader Editor designed for an AI-assisted workflow.

Nowadays, we often ask AI to help with shader coding. For node-based shader users, we can use a "Custom Expression" node, but manually adding inputs and outputs is tedious and annoying—especially when iterating through different versions of code. This helper simplifies that process entirely.

#### How it Works:
- **AI-Ready Workflow**: When asking an AI for shader code, you provide it with the port generation guidelines (found in the [spec](AmplifyExtension/AutoParsedExpressionNode_Spec.md)).
- **Instant Port Generation**: Once the AI generates the code with `//@in` and `//@out` comments, you simply paste it into the node.
- **No Manual Setup**: The node automatically parses the code and creates all necessary ports instantly. No more spending 5 minutes manually setting up ports for every iteration.
- **Function Isolation**: It automatically prefixes functions to prevent naming conflicts.


### 2. Custom Attributes: Button Attribute

Located in `CustomAttributes/`, this tool provides a simple way to add buttons to your Inspector for `MonoBehaviour` and `ScriptableObject` classes.

#### How it Works:
- **`[Button]` Attribute**: Simply add the `[Button]` attribute to any method with no parameters in your script.
- **Automatic Rendering**: The tool includes a custom editor that automatically scans for methods with the `[Button]` attribute and renders them as clickable buttons at the bottom of the Inspector.
- **Custom Labels**: You can optionally provide a label for the button: `[Button("Click Me")]`. If no label is provided, the method name is used.

- **Multi-Object Editing**: Supports multi-object editing, allowing you to trigger methods on all selected objects at once.
